# Donut Lock

Native platform apps live in `macOS` and `windows`. The square lock image in `Photos` is used across both app interfaces and as the Windows executable/macOS app bundle icon.

Both apps support password unlock with a minimum of 4 characters, encrypted vault copies of files and folders, and platform biometric unlock when configured. The macOS app uses Touch ID; the Windows app uses Windows Hello and DPAPI. Images, executable files, and other file types are encrypted as file data.

These apps authenticate items opened through their vault. They do not intercept arbitrary Finder or File Explorer opens, and the original item remains in place. See each platform README for build instructions and details.
