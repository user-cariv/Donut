$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$picker = New-Object System.Windows.Forms.FolderBrowserDialog
$picker.Description = 'Choose the folder where Donut Boost is installed or extracted.'
$picker.ShowNewFolderButton = $false
$picker.SelectedPath = $PSScriptRoot
if ($picker.ShowDialog() -ne [System.Windows.Forms.DialogResult]::OK) { exit 0 }

$selectedFullPath = [System.IO.Path]::GetFullPath($picker.SelectedPath)
$installPath = $selectedFullPath.TrimEnd('\')
$blockedRoots = @(
    [System.IO.Path]::GetPathRoot($selectedFullPath),
    [Environment]::GetFolderPath('UserProfile'),
    (Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads'),
    [Environment]::GetFolderPath('Windows'),
    [Environment]::GetFolderPath('ProgramFiles'),
    ${env:ProgramFiles(x86)}
) | Where-Object { $_ } | ForEach-Object { [System.IO.Path]::GetFullPath($_).TrimEnd('\') }
if ($installPath -in $blockedRoots) {
    [System.Windows.Forms.MessageBox]::Show('Choose the Donut Boost app folder itself, not a drive, Windows, Program Files, your profile, or your whole Downloads folder.', 'Choose the app folder', 'OK', 'Warning') | Out-Null
    exit 1
}
$exePath = Join-Path $installPath 'DonutBoost.exe'
$dllPath = Join-Path $installPath 'DonutBoost.dll'
if (-not (Test-Path -LiteralPath $exePath) -or -not (Test-Path -LiteralPath $dllPath)) {
    [System.Windows.Forms.MessageBox]::Show('That folder does not contain Donut Boost. Choose the folder containing DonutBoost.exe and DonutBoost.dll.', 'Donut Boost', 'OK', 'Warning') | Out-Null
    exit 1
}

$running = Get-Process -Name 'DonutBoost' -ErrorAction SilentlyContinue | Where-Object {
    try { [System.IO.Path]::GetFullPath($_.Path).StartsWith($installPath + '\', [System.StringComparison]::OrdinalIgnoreCase) } catch { $false }
}
if ($running) {
    [System.Windows.Forms.MessageBox]::Show('Close Donut Boost and run this uninstaller again.', 'Donut Boost is running', 'OK', 'Information') | Out-Null
    exit 1
}

$unexpected = @(Get-ChildItem -LiteralPath $installPath -Force | Where-Object {
    $_.PSIsContainer -or $_.Name -notin @(
        'DonutBoost.exe', 'DonutBoost.dll', 'DonutBoost.deps.json', 'DonutBoost.runtimeconfig.json',
        'DonutBoost.pdb', 'Donut.ico', 'DonutBoostLogo.png', 'Fix-Network-Priority.bat',
        'Launch Donut Boost.bat', 'Launch Donut Boost.vbs', 'Uninstall Donut Boost.bat',
        'Uninstall Donut Boost.ps1'
    ) -and $_.Name -notmatch '^DonutBoost\.(exe|dll)\.old(?:\..*)?$'
})
$deleteWholeFolder = $unexpected.Count -eq 0
if ($unexpected.Count -gt 0) {
    $extraNames = ($unexpected | Select-Object -First 8 | ForEach-Object { $_.Name }) -join "`r`n"
    $choice = [System.Windows.Forms.MessageBox]::Show(
        "This folder contains other files or folders:`r`n`r`n$extraNames`r`n`nChoose Yes to remove the entire selected folder. Choose No to remove Donut Boost files only and keep the other items.",
        'Other files found', 'YesNo', 'Warning'
    )
    $deleteWholeFolder = $choice -eq [System.Windows.Forms.DialogResult]::Yes
}

$downloadsPath = Join-Path ([Environment]::GetFolderPath('UserProfile')) 'Downloads\DonutBoost'
$confirm = [System.Windows.Forms.MessageBox]::Show(
    "Remove Donut Boost and its saved activation/trial files?`r`n`r`nInstall folder: $installPath`r`nDownloaded files are kept unless you choose to remove them in the next prompt.",
    'Uninstall Donut Boost', 'YesNo', 'Warning'
)
if ($confirm -ne [System.Windows.Forms.DialogResult]::Yes) { exit 0 }

$removeDownloads = [System.Windows.Forms.MessageBox]::Show(
    "Also permanently delete the files Donut Boost saved here?`r`n`r`n$downloadsPath",
    'Remove downloaded files?', 'YesNo', 'Question'
) -eq [System.Windows.Forms.DialogResult]::Yes

$helperPath = Join-Path $env:TEMP ('DonutBoost-cleanup-' + [guid]::NewGuid().ToString('N') + '.ps1')
$helper = @'
param([string]$FolderBase64, [bool]$DeleteWholeFolder, [int]$WaitPid)
$ErrorActionPreference = 'Stop'
try { Wait-Process -Id $WaitPid -Timeout 30 -ErrorAction SilentlyContinue } catch {}
Start-Sleep -Milliseconds 700
$target = [System.Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($FolderBase64))
$known = @('DonutBoost.exe','DonutBoost.dll','DonutBoost.deps.json','DonutBoost.runtimeconfig.json','DonutBoost.pdb','Donut.ico','DonutBoostLogo.png','Fix-Network-Priority.bat','Launch Donut Boost.bat','Launch Donut Boost.vbs','Uninstall Donut Boost.bat','Uninstall Donut Boost.ps1')
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try {
        if ($DeleteWholeFolder) {
            Remove-Item -LiteralPath $target -Recurse -Force
        } elseif (Test-Path -LiteralPath $target) {
            Get-ChildItem -LiteralPath $target -Force -File | Where-Object { $_.Name -in $known -or $_.Name -match '^DonutBoost\.(exe|dll)\.old(?:\..*)?$' } | Remove-Item -Force
            if (@(Get-ChildItem -LiteralPath $target -Force).Count -eq 0) { Remove-Item -LiteralPath $target -Force }
        }
        break
    } catch { Start-Sleep -Milliseconds 500 }
}
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
'@

[System.IO.File]::WriteAllText($helperPath, $helper, [System.Text.Encoding]::Unicode)
$folderToken = [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($installPath))
$argumentLine = '-NoLogo -NoProfile -ExecutionPolicy Bypass -File "' + $helperPath + '" -FolderBase64 ' + $folderToken + ' -DeleteWholeFolder ' + $deleteWholeFolder.ToString().ToLowerInvariant() + ' -WaitPid ' + $PID
$programFiles = @([Environment]::GetFolderPath('ProgramFiles'), ${env:ProgramFiles(x86)}) | Where-Object { $_ }
$needsElevation = $false
foreach ($protectedRoot in $programFiles) {
    if ($installPath.StartsWith($protectedRoot.TrimEnd('\') + '\', [System.StringComparison]::OrdinalIgnoreCase)) { $needsElevation = $true }
}
try {
    Set-Location -LiteralPath $env:TEMP
    if ($needsElevation) {
        Start-Process -FilePath 'powershell.exe' -Verb RunAs -ArgumentList $argumentLine | Out-Null
    } else {
        Start-Process -FilePath 'powershell.exe' -WindowStyle Hidden -ArgumentList $argumentLine | Out-Null
    }
} catch {
    Remove-Item -LiteralPath $helperPath -Force -ErrorAction SilentlyContinue
    [System.Windows.Forms.MessageBox]::Show('Uninstall was cancelled or could not start. No saved app data was removed.', 'Donut Boost', 'OK', 'Information') | Out-Null
    exit 1
}

$appDataPath = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'DonutBoost'
if (Test-Path -LiteralPath $appDataPath) { Remove-Item -LiteralPath $appDataPath -Recurse -Force }
Remove-Item -LiteralPath 'Registry::HKEY_CURRENT_USER\Software\DonutBoost' -Recurse -Force -ErrorAction SilentlyContinue
$anchorKeyPath = 'Software\Microsoft\Windows\CurrentVersion\Explorer\CLSID\{D090B005-7A8E-4F23-9112-E59C778B5D01}'
$anchorKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($anchorKeyPath, $true)
if ($anchorKey) {
    try {
        $anchorKey.DeleteValue('Anchor', $false)
        $anchorKeyIsEmpty = $anchorKey.ValueCount -eq 0 -and $anchorKey.SubKeyCount -eq 0
    } finally { $anchorKey.Close() }
    if ($anchorKeyIsEmpty) { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($anchorKeyPath, $false) }
}
if ($removeDownloads -and (Test-Path -LiteralPath $downloadsPath)) { Remove-Item -LiteralPath $downloadsPath -Recurse -Force }

[System.Windows.Forms.MessageBox]::Show('Donut Boost saved data is removed. Its app files will finish deleting after this window closes.', 'Donut Boost removed', 'OK', 'Information') | Out-Null
