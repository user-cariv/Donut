using System.Collections.ObjectModel;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using Microsoft.Win32;
using System.IO.Compression;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows.Interop;
using Windows.Security.Credentials.UI;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;

namespace DonutLock;
public sealed class VaultEntry { public string Name { get; set; } = ""; public string Path { get; set; } = ""; public bool IsFolder { get; set; } public bool HasBiometric { get; set; } }
public partial class MainWindow : Window
{
    readonly ObservableCollection<VaultEntry> entries = new();
    readonly string root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DonutLock", "Vault");
    string IndexPath => System.IO.Path.Combine(root, "index.json");
    public MainWindow() { InitializeComponent(); Directory.CreateDirectory(root); Items.ItemsSource = entries; LoadIndex(); }
    void LoadIndex() { try { foreach (var e in JsonSerializer.Deserialize<List<VaultEntry>>(File.ReadAllText(IndexPath)) ?? new()) entries.Add(e); } catch { } Refresh(); }
    void SaveIndex() { File.WriteAllText(IndexPath, JsonSerializer.Serialize(entries)); Refresh(); }
    void Refresh() { CountText.Text = $"{entries.Count} items"; EmptyState.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed; }
    async void Add_Click(object sender, RoutedEventArgs e)
    {
        var choose = MessageBox.Show("Choose Yes to encrypt a folder, or No to select files.", "Add to Donut Lock", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
        if (choose == MessageBoxResult.Cancel) return;
        var selected = new List<(string path, bool folder)>();
        if (choose == MessageBoxResult.Yes) { using var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose a folder to encrypt" }; if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return; selected.Add((dlg.SelectedPath, true)); }
        else { var dlg = new OpenFileDialog { Title = "Choose files to encrypt", Multiselect = true, Filter = "All files (*.*)|*.*" }; if (dlg.ShowDialog() != true) return; selected.AddRange(dlg.FileNames.Select(f => (f, false))); }
        var password = Password("Create a vault password (minimum 4 characters)", true); if (password is null) return;
        var helloAvailable = await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available;
        var enableHello = helloAvailable && MessageBox.Show("Use Windows Hello to unlock vault items without retyping the vault password? If a fingerprint is not enrolled, set it up in Settings > Accounts > Sign-in options. Windows may use your Hello PIN when fingerprint is unavailable.", "Enable biometric unlock", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        if (!helloAvailable) MessageBox.Show("Windows Hello fingerprint unlock is not configured. Set it up in Settings > Accounts > Sign-in options. You can continue with your password.", "Biometric setup suggestion", MessageBoxButton.OK, MessageBoxImage.Information);
        try {
            foreach (var item in selected) {
                var clearPath = item.path; var archivePath = "";
                if (item.folder) { archivePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid() + ".zip"); ZipFile.CreateFromDirectory(item.path, archivePath, CompressionLevel.Optimal, true); clearPath = archivePath; }
                var clear = File.ReadAllBytes(clearPath); if (archivePath != "") File.Delete(archivePath);
                var salt = RandomNumberGenerator.GetBytes(16); var nonce = RandomNumberGenerator.GetBytes(12); var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, 180000, HashAlgorithmName.SHA256, 32); var cipher = new byte[clear.Length]; var tag = new byte[16]; using (var aes = new AesGcm(key, 16)) aes.Encrypt(nonce, clear, cipher, tag);
                var id = Guid.NewGuid().ToString("N") + ".vault"; File.WriteAllBytes(System.IO.Path.Combine(root, id), salt.Concat(nonce).Concat(tag).Concat(cipher).ToArray());
                if (enableHello) { var secret = ProtectedData.Protect(System.Text.Encoding.UTF8.GetBytes(password), salt, DataProtectionScope.CurrentUser); File.WriteAllBytes(System.IO.Path.Combine(root, id + ".hello"), secret); }
                entries.Insert(0, new VaultEntry { Name = System.IO.Path.GetFileName(item.path), Path = id, IsFolder = item.folder, HasBiometric = enableHello });
            }
            SaveIndex(); StatusText.Text = "Encrypted copies added. The original items remain in place.";
        } catch (Exception ex) { StatusText.Text = "Could not encrypt item: " + ex.Message; }
    }
    void Unlock_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not VaultEntry item) return;
        _ = UnlockItem(item);
    }
    async Task UnlockItem(VaultEntry item) {
        var status = await UserConsentVerifier.CheckAvailabilityAsync();
        string? password = null;
        if (status == UserConsentVerifierAvailability.Available && item.HasBiometric) {
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(new WindowInteropHelper(this).Handle, "Unlock " + item.Name);
            if (result == UserConsentVerificationResult.Verified) { try { var vaultFile = File.ReadAllBytes(System.IO.Path.Combine(root, item.Path)); var protectedPassword = File.ReadAllBytes(System.IO.Path.Combine(root, item.Path + ".hello")); password = System.Text.Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedPassword, vaultFile[..16], DataProtectionScope.CurrentUser)); } catch { } }
        } else if (status != UserConsentVerifierAvailability.Available) {
            MessageBox.Show("Windows Hello or a fingerprint reader is not set up. Set it up in Settings > Accounts > Sign-in options, or continue with your vault password.", "Biometric sign-in unavailable", MessageBoxButton.OK, MessageBoxImage.Information);
        } else {
            StatusText.Text = "Use your vault password to unlock this item.";
        }
        password ??= Password("Enter the password used for this vault copy", false); if (password is null) return;
        try { var bytes = File.ReadAllBytes(System.IO.Path.Combine(root, item.Path)); var key = Rfc2898DeriveBytes.Pbkdf2(password, bytes[..16], 180000, HashAlgorithmName.SHA256, 32); var clear = new byte[bytes.Length - 44]; using (var aes = new AesGcm(key, 16)) aes.Decrypt(bytes[16..28], bytes[44..], bytes[28..44], clear); var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), item.Name + (item.IsFolder ? ".zip" : "")); File.WriteAllBytes(temp, clear); if (item.IsFolder) { var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "DonutLock-" + Guid.NewGuid().ToString("N")); ZipFile.ExtractToDirectory(temp, dir); File.Delete(temp); System.Diagnostics.Process.Start("explorer.exe", dir); } else System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(temp) { UseShellExecute = true }); StatusText.Text = "Opened a temporary decrypted copy. Remove it after use."; } catch { StatusText.Text = "Unlock failed. Check the password or vault data."; }
    }
    void Delete_Click(object sender, RoutedEventArgs e) { if ((sender as FrameworkElement)?.DataContext is VaultEntry item) { try { File.Delete(System.IO.Path.Combine(root, item.Path)); File.Delete(System.IO.Path.Combine(root, item.Path + ".hello")); } catch { } entries.Remove(item); SaveIndex(); } }
    static string? Password(string prompt, bool creating) { var w = new PasswordWindow(prompt, creating) { Owner = System.Windows.Application.Current.MainWindow }; return w.ShowDialog() == true ? w.PasswordValue : null; }
}
