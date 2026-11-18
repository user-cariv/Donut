#!/bin/bash
set -euo pipefail

PROJECT_DIR="$(cd "$(dirname "$0")" && pwd)"
APP_NAME="Donut Lock.app"
APP_DIR="$PROJECT_DIR/build/$APP_NAME"

cd "$PROJECT_DIR"
swift build -c release
rm -rf "$APP_DIR"
mkdir -p "$APP_DIR/Contents/MacOS" "$APP_DIR/Contents/Resources"
cp "$PROJECT_DIR/.build/release/DonutLock" "$APP_DIR/Contents/MacOS/DonutLock"
cp -R "$PROJECT_DIR/.build/release/DonutLock_DonutLock.bundle" "$APP_DIR/Contents/Resources/DonutLock_DonutLock.bundle"
cp "$PROJECT_DIR/Assets/DonutLock.icns" "$APP_DIR/Contents/Resources/DonutLock.icns"
cp "$PROJECT_DIR/Assets/donut-lock-logo.jpeg" "$APP_DIR/Contents/Resources/donut-lock-logo.jpeg"
cat > "$APP_DIR/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>DonutLock</string>
  <key>CFBundleIdentifier</key><string>com.donut.lock</string>
  <key>CFBundleName</key><string>Donut Lock</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleIconFile</key><string>DonutLock.icns</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
</dict>
</plist>
PLIST
echo "Created $APP_DIR"
