using System.Windows;
namespace DonutLock;
public partial class PasswordWindow : Window
{
    public string PasswordValue => Input.Password;
    readonly bool creating;
    public PasswordWindow(string prompt, bool creating) { InitializeComponent(); Prompt.Text = prompt; this.creating = creating; Loaded += (_, _) => Input.Focus(); }
    void Continue_Click(object sender, RoutedEventArgs e) { if (Input.Password.Length < (creating ? 4 : 1)) { System.Windows.MessageBox.Show(creating ? "Use at least 4 characters. Letters and numbers are allowed." : "Enter your password.", "Password required", MessageBoxButton.OK, MessageBoxImage.Information); return; } DialogResult = true; }
}
