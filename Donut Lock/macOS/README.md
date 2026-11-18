# Donut Lock for macOS

Native SwiftUI vault for macOS 13 or newer. On a Mac, run `./build-macos-app.sh` to build `build/Donut Lock.app` with the Donut Lock logo as its Finder/Dock app icon. The app interface uses the same logo.

Files and folders selected in the app are encrypted with AES-GCM. Passwords must contain at least 4 characters; letters, numbers, and symbols are accepted. On a Mac with Touch ID configured, users can opt in to Touch ID unlock. The password is stored in Keychain with access restricted to the current enrolled biometric set. If Touch ID is unavailable or not enrolled, the app suggests System Settings > Touch ID & Password and allows password unlock.

Folders are archived for encryption and extracted to a temporary folder when unlocked. Other file types, including images and executable files, are stored as bytes and opened with the default app.

This vault protects encrypted copies opened through Donut Lock. It does not intercept Finder or other apps' open actions, and it leaves originals in place. For actual protection, move sensitive data out of its original location after confirming the vault copy works. Temporary decrypted copies should be removed after use.
