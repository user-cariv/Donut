# Donut Lock for Windows

Native WPF app for Windows 10 version 22H2 or newer. Build with the .NET 8 SDK using `dotnet build DonutLock.csproj`; the resulting executable embeds the Donut Lock logo as its Windows app icon. The app interface uses the same logo.

The app encrypts files and folders with AES-GCM. Passwords must contain at least 4 characters; letters, numbers, and symbols are accepted. Images, executable files, and other file types are encrypted as bytes. When the user opts in and Windows Hello is available, Windows Hello verifies the user and DPAPI protects the saved vault password for biometric unlock. If Hello is unavailable, the app suggests Settings > Accounts > Sign-in options and uses the vault password. Windows Hello may use a configured Hello PIN when fingerprint authentication is unavailable.

Folders are archived during encryption and extracted to a temporary folder when unlocked. Files open in their default app; folders open in Explorer. Temporary decrypted copies should be removed after use.

This vault protects encrypted copies opened through Donut Lock. It does not intercept File Explorer or other apps' open actions, and it leaves originals in place. Move sensitive data out of its original location after confirming the vault copy works.
