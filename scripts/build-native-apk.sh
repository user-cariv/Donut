#!/usr/bin/env bash
set -e

echo "=== Building Real Android APK for donut convert ==="
WORK_DIR="/tmp/apk_builder"
rm -rf "$WORK_DIR"
mkdir -p "$WORK_DIR"/{gen,obj,src/com/donutconvert/app,res/values,res/drawable,assets/www}

# Copy React built assets
cp -r dist/* "$WORK_DIR/assets/www/"
# Remove any existing apks or zips from assets
rm -f "$WORK_DIR/assets/www/"*.apk "$WORK_DIR/assets/www/"*.zip

# Copy icon
if [ -f "public/donut-icon.png" ]; then
  cp "public/donut-icon.png" "$WORK_DIR/res/drawable/ic_launcher.png"
elif [ -f "public/donut.svg" ]; then
  # if only svg exists, check for any png or create a simple fallback
  cp "public/donut-icon.png" "$WORK_DIR/res/drawable/ic_launcher.png" 2>/dev/null || true
fi

# If no launcher icon in drawable, create a 192x192 PNG
if [ ! -f "$WORK_DIR/res/drawable/ic_launcher.png" ]; then
  # Check if public has any png
  PNG_FOUND=$(find public -name "*.png" | head -n 1)
  if [ -n "$PNG_FOUND" ]; then
    cp "$PNG_FOUND" "$WORK_DIR/res/drawable/ic_launcher.png"
  else
    # Create a 1x1 or basic png using python
    python3 -c "
import zlib, struct
w, h = 96, 96
raw_data = b''.join([b'\x00' + b'\xff\x98\x00\xff' * w for _ in range(h)])
def make_png(w, h, raw):
    png = b'\x89PNG\r\n\x1a\n'
    def chunk(tag, data):
        return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data) & 0xffffffff)
    ihdr = struct.pack('>IIBBBBB', w, h, 8, 6, 0, 0, 0)
    png += chunk(b'IHDR', ihdr)
    png += chunk(b'IDAT', zlib.compress(raw))
    png += chunk(b'IEND', b'')
    return png
with open('$WORK_DIR/res/drawable/ic_launcher.png', 'wb') as f:
    f.write(make_png(w, h, raw_data))
"
  fi
fi

# Strings resource
cat << 'EOF' > "$WORK_DIR/res/values/strings.xml"
<?xml version="1.0" encoding="utf-8"?>
<resources>
    <string name="app_name">donut convert</string>
</resources>
EOF

# AndroidManifest.xml
cat << 'EOF' > "$WORK_DIR/AndroidManifest.xml"
<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="com.donutconvert.app"
    android:versionCode="3"
    android:versionName="1.3.0">

    <uses-sdk android:minSdkVersion="36" android:targetSdkVersion="36" />

    <uses-permission android:name="android.permission.INTERNET" />
    <uses-permission android:name="android.permission.ACCESS_NETWORK_STATE" />
    <uses-permission android:name="android.permission.READ_MEDIA_IMAGES" />
    <uses-permission android:name="android.permission.READ_MEDIA_VISUAL_USER_SELECTED" />
    <uses-permission android:name="android.permission.CAMERA" />
    <uses-feature android:name="android.hardware.camera" android:required="false" />
    <uses-feature android:name="android.hardware.camera.autofocus" android:required="false" />

    <application
        android:label="@string/app_name"
        android:icon="@drawable/ic_launcher"
        android:hardwareAccelerated="true"
        android:allowBackup="true"
        android:supportsRtl="true"
        android:theme="@android:style/Theme.DeviceDefault.NoActionBar">

        <activity
            android:name="com.donutconvert.app.MainActivity"
            android:label="@string/app_name"
            android:exported="true"
            android:configChanges="orientation|screenSize|keyboardHidden"
            android:windowSoftInputMode="adjustResize">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
    </application>
</manifest>
EOF

# MainActivity.java with full WebView + File Upload Support
cat << 'EOF' > "$WORK_DIR/src/com/donutconvert/app/MainActivity.java"
package com.donutconvert.app;

import android.app.Activity;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.view.KeyEvent;
import android.webkit.ValueCallback;
import android.webkit.WebChromeClient;
import android.webkit.WebSettings;
import android.webkit.WebView;
import android.webkit.WebViewClient;
import android.webkit.PermissionRequest;

public class MainActivity extends Activity {
    private WebView webView;
    private ValueCallback<Uri[]> filePathCallback;
    private final static int FILECHOOSER_RESULTCODE = 101;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);

        webView = new WebView(this);
        setContentView(webView);

        WebSettings settings = webView.getSettings();
        settings.setJavaScriptEnabled(true);
        settings.setDomStorageEnabled(true);
        settings.setDatabaseEnabled(true);
        settings.setAllowFileAccess(true);
        settings.setAllowContentAccess(true);
        settings.setAllowFileAccessFromFileURLs(true);
        settings.setAllowUniversalAccessFromFileURLs(true);
        settings.setLoadWithOverviewMode(true);
        settings.setUseWideViewPort(true);
        settings.setBuiltInZoomControls(false);
        settings.setDisplayZoomControls(false);

        webView.setWebViewClient(new WebViewClient() {
            @Override
            public boolean shouldOverrideUrlLoading(WebView view, String url) {
                if (url.startsWith("file://") || url.startsWith("blob:") || url.startsWith("data:")) {
                    return false;
                }
                try {
                    Intent intent = new Intent(Intent.ACTION_VIEW, Uri.parse(url));
                    startActivity(intent);
                    return true;
                } catch (Exception e) {
                    return false;
                }
            }
        });

        webView.setWebChromeClient(new WebChromeClient() {
            @Override
            public void onPermissionRequest(final PermissionRequest request) {
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
                    request.grant(request.getResources());
                }
            }

            @Override
            public boolean onShowFileChooser(WebView webView, ValueCallback<Uri[]> filePathCallback, WebChromeClient.FileChooserParams fileChooserParams) {
                if (MainActivity.this.filePathCallback != null) {
                    MainActivity.this.filePathCallback.onReceiveValue(null);
                }
                MainActivity.this.filePathCallback = filePathCallback;

                Intent intent = null;
                if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.LOLLIPOP) {
                    intent = fileChooserParams.createIntent();
                } else {
                    intent = new Intent(Intent.ACTION_GET_CONTENT);
                    intent.setType("*/*");
                }
                try {
                    startActivityForResult(Intent.createChooser(intent, "Select Files to Convert"), FILECHOOSER_RESULTCODE);
                } catch (Exception e) {
                    MainActivity.this.filePathCallback = null;
                    return false;
                }
                return true;
            }
        });

        webView.loadUrl("file:///android_asset/www/index.html");
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        if (requestCode == FILECHOOSER_RESULTCODE) {
            if (filePathCallback == null) return;
            Uri[] results = null;
            if (resultCode == Activity.RESULT_OK && data != null) {
                if (data.getClipData() != null) {
                    int count = data.getClipData().getItemCount();
                    results = new Uri[count];
                    for (int i = 0; i < count; i++) {
                        results[i] = data.getClipData().getItemAt(i).getUri();
                    }
                } else if (data.getData() != null) {
                    results = new Uri[]{ data.getData() };
                }
            }
            filePathCallback.onReceiveValue(results);
            filePathCallback = null;
        } else {
            super.onActivityResult(requestCode, resultCode, data);
        }
    }

    @Override
    public boolean onKeyDown(int keyCode, KeyEvent event) {
        if (keyCode == KeyEvent.KEYCODE_BACK && webView.canGoBack()) {
            webView.goBack();
            return true;
        }
        return super.onKeyDown(keyCode, event);
    }
}
EOF

ANDROID_JAR="/usr/lib/android-sdk/platforms/android-23/android.jar"
DX="/usr/lib/android-sdk/build-tools/debian/dx"

echo "1. Generating R.java with aapt..."
aapt package -f -m \
  -J "$WORK_DIR/gen" \
  -M "$WORK_DIR/AndroidManifest.xml" \
  -S "$WORK_DIR/res" \
  -I "$ANDROID_JAR"

echo "2. Compiling Java sources with javac..."
javac -source 1.8 -target 1.8 \
  -bootclasspath "$ANDROID_JAR" \
  -cp "$ANDROID_JAR" \
  -d "$WORK_DIR/obj" \
  "$WORK_DIR/gen/com/donutconvert/app/R.java" \
  "$WORK_DIR/src/com/donutconvert/app/MainActivity.java"

echo "3. Converting bytecode to Dalvik classes.dex with dx..."
$DX --dex --output="$WORK_DIR/classes.dex" "$WORK_DIR/obj"

echo "4. Packaging APK with aapt..."
cd "$WORK_DIR"
aapt package -f \
  -M AndroidManifest.xml \
  -S res \
  -A assets \
  -I "$ANDROID_JAR" \
  -F app-unaligned.apk

echo "5. Adding classes.dex to APK..."
aapt add app-unaligned.apk classes.dex

echo "6. Aligning APK with zipalign..."
zipalign -v -p 4 app-unaligned.apk app-aligned.apk

echo "7. Checking release signing configuration..."
: "${RELEASE_KEYSTORE:?Set RELEASE_KEYSTORE to a protected release keystore path}"
: "${RELEASE_KEY_ALIAS:?Set RELEASE_KEY_ALIAS to the release key alias}"
: "${RELEASE_KEYSTORE_PASSWORD:?Set RELEASE_KEYSTORE_PASSWORD in the build environment}"
: "${RELEASE_KEY_PASSWORD:?Set RELEASE_KEY_PASSWORD in the build environment}"

echo "8. Signing APK with apksigner (v1, v2, v3)..."
apksigner sign \
  --ks "$RELEASE_KEYSTORE" \
  --ks-key-alias "$RELEASE_KEY_ALIAS" \
  --ks-pass env:RELEASE_KEYSTORE_PASSWORD \
  --key-pass env:RELEASE_KEY_PASSWORD \
  --out "$WORK_DIR/Donut Convert.apk" \
  app-aligned.apk

echo "9. Verifying signed APK with apksigner..."
apksigner verify --verbose "$WORK_DIR/Donut Convert.apk"

echo "10. Copying output to public and dist..."
cd -
cp "$WORK_DIR/Donut Convert.apk" "public/Donut Convert.apk"
cp "$WORK_DIR/Donut Convert.apk" "public/DonutConvert.apk"
cp "$WORK_DIR/Donut Convert.apk" "dist/Donut Convert.apk"
cp "$WORK_DIR/Donut Convert.apk" "dist/DonutConvert.apk"

ls -lh "public/Donut Convert.apk"
echo "=== Native APK Build Completed Successfully! ==="
