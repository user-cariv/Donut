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
ICON_SOURCE="${ICON_SOURCE:-Donut Island Sunset Oasis.png}"

if [[ ! -f "$ICON_SOURCE" ]]; then
  echo "App icon source not found: $ICON_SOURCE"
  echo "Place the Donut Island logo beside build.sh, or set ICON_SOURCE to its path."
  exit 1
fi

echo "▸ Compiling (release)…"
swift build -c release
BIN="$(swift build -c release --show-bin-path)/DonutIsland"

echo "▸ Assembling ${APP}…"
rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp "$BIN" "$APP/Contents/MacOS/DonutIsland"

echo "Creating app icon from $ICON_SOURCE..."
ICONSET="$APP/Contents/Resources/DonutIslandIcon.iconset"
mkdir -p "$ICONSET"
sips -s format png -z 16 16 "$ICON_SOURCE" --out "$ICONSET/icon_16x16.png" >/dev/null
sips -s format png -z 32 32 "$ICON_SOURCE" --out "$ICONSET/icon_16x16@2x.png" >/dev/null
sips -s format png -z 32 32 "$ICON_SOURCE" --out "$ICONSET/icon_32x32.png" >/dev/null
sips -s format png -z 64 64 "$ICON_SOURCE" --out "$ICONSET/icon_32x32@2x.png" >/dev/null
sips -s format png -z 128 128 "$ICON_SOURCE" --out "$ICONSET/icon_128x128.png" >/dev/null
sips -s format png -z 256 256 "$ICON_SOURCE" --out "$ICONSET/icon_128x128@2x.png" >/dev/null
sips -s format png -z 256 256 "$ICON_SOURCE" --out "$ICONSET/icon_256x256.png" >/dev/null
sips -s format png -z 512 512 "$ICON_SOURCE" --out "$ICONSET/icon_256x256@2x.png" >/dev/null
sips -s format png -z 512 512 "$ICON_SOURCE" --out "$ICONSET/icon_512x512.png" >/dev/null
sips -s format png -z 1024 1024 "$ICON_SOURCE" --out "$ICONSET/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/DonutIslandIcon.icns"
rm -rf "$ICONSET"

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
  <key>CFBundleShortVersionString</key><string>1.0.16</string>
  <key>CFBundleVersion</key><string>16</string>
  <key>CFBundleIconFile</key><string>DonutIslandIcon</string>
  <key>LSMinimumSystemVersion</key><string>27.0</string>
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

echo "✓ Built $(pwd)/$APP"

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
