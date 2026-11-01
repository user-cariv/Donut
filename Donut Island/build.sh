#!/bin/bash
# Builds DonutIsland.app from source.
#   ./build.sh            -> creates ./DonutIsland.app
#   ./build.sh --install  -> also copies it to /Applications and launches it
set -euo pipefail
cd "$(dirname "$0")"

if ! command -v swift >/dev/null 2>&1; then
  echo "Swift toolchain not found."
  echo "Install it once with:  xcode-select --install   (then re-run ./build.sh)"
  exit 1
fi

APP="Donut Island.app"

echo "▸ Compiling (release)…"
swift build -c release
BIN="$(swift build -c release --show-bin-path)/DonutIsland"

echo "▸ Assembling ${APP}…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/DonutIsland"

cat > "$APP/Contents/Info.plist" <<'PLIST'
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
  <key>CFBundleExecutable</key><string>DonutIsland</string>
  <key>CFBundleIdentifier</key><string>com.ariv.donutisland</string>
  <key>CFBundleName</key><string>Donut Island</string>
  <key>CFBundleDisplayName</key><string>Donut Island</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>1.0</string>
  <key>CFBundleVersion</key><string>1</string>
  <key>LSMinimumSystemVersion</key><string>13.0</string>
  <key>LSUIElement</key><true/>
  <key>NSPrincipalClass</key><string>NSApplication</string>
  <key>NSHighResolutionCapable</key><true/>
  <key>NSCalendarsUsageDescription</key>
  <string>Donut Island shows your upcoming events in the island.</string>
  <key>NSCalendarsFullAccessUsageDescription</key>
  <string>Donut Island shows your upcoming events in the island.</string>
</dict>
</plist>
PLIST

echo "▸ Signing (ad-hoc)…"
codesign --force --deep --sign - --identifier com.ariv.donutisland "$APP"

echo "✓ Built $(pwd)/${APP}"

if [[ "${1:-}" == "--install" ]]; then
  echo "▸ Installing to /Applications…"
  pkill -x DonutIsland 2>/dev/null || true
  rm -rf "/Applications/$APP"
  cp -R "$APP" "/Applications/$APP"
  open "/Applications/$APP"
  echo "✓ Running. Look for the 🍩 in the menu bar."
else
  echo "Run it with:  open $APP     (or ./build.sh --install to put it in /Applications)"
fi
