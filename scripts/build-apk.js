import fs from 'fs';
import path from 'path';
import JSZip from 'jszip';

throw new Error(
  'The stub APK builder has been removed because it does not produce a valid signed Android application. ' +
  'Build with scripts/build-native-apk.sh or the Android Studio project instead.'
);

async function generateApk() {
  const zip = new JSZip();

  // Android Manifest with Camera, Storage, Vibration, and System Alert permissions
  const manifest = `<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android"
    package="com.donut.convert"
    android:versionCode="3"
    android:versionName="1.3.0">
    <uses-sdk android:minSdkVersion="36" android:targetSdkVersion="36" />
    <uses-permission android:name="android.permission.CAMERA" />
    <uses-feature android:name="android.hardware.camera" android:required="false" />
    <uses-permission android:name="android.permission.READ_MEDIA_IMAGES" />
    <uses-permission android:name="android.permission.READ_MEDIA_VISUAL_USER_SELECTED" />
    <uses-permission android:name="android.permission.SYSTEM_ALERT_WINDOW" />
    <uses-permission android:name="android.permission.VIBRATE" />
    <uses-permission android:name="android.permission.INTERNET" />
    <application
        android:allowBackup="true"
        android:label="donut convert"
        android:icon="@mipmap/ic_launcher"
        android:supportsRtl="true"
        android:theme="@android:style/Theme.DeviceDefault.NoActionBar">
        <activity
            android:name="com.donut.convert.MainActivity"
            android:exported="true"
            android:configChanges="orientation|screenSize|keyboardHidden">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>
    </application>
</manifest>`;

  zip.file('AndroidManifest.xml', manifest);

  // META-INF
  zip.file(
    'META-INF/MANIFEST.MF',
    `Manifest-Version: 1.0\nCreated-By: Donut Convert APK Studio Builder 1.3.0\nBuilt-By: user-cariv\nAuthor: arivchoudhary16112007@gmail.com\n\nName: AndroidManifest.xml\nSHA-256-Digest: d4e5f6g7h8i9j0a1b2c3\n\nName: classes.dex\nSHA-256-Digest: e5f6g7h8i9j0a1b2c3d4\n`
  );
  zip.file('META-INF/CERT.SF', `Signature-Version: 1.0\nCreated-By: 1.3.0 (Android Studio / Donut Convert)\nSHA-256-Digest-Manifest: f6g7h8i9j0a1b2c3d4e5\n`);
  zip.file('META-INF/CERT.RSA', Buffer.from([0x30, 0x82, 0x01, 0x0a, 0x02, 0x82, 0x01, 0x01]));

  // DEX Bytecode stub
  const dexHeader = Buffer.alloc(112);
  dexHeader.write('dex\n039\0', 0, 8, 'binary');
  zip.file('classes.dex', dexHeader);

  // Resources
  zip.file('resources.arsc', Buffer.from([0x02, 0x00, 0x0c, 0x00]));
  
  // App Config & Metadata
  zip.file(
    'assets/donut_config.json',
    JSON.stringify(
      {
        appName: 'donut convert',
        version: '1.3.0',
        versionCode: 3,
        author: 'arivchoudhary16112007@gmail.com',
        website: 'https://user-cariv.github.io/Donut-Convert/',
        features: [
          'Numbered multi-select sequence ordering (1, 2, 3...)',
          'Radial donut format selector (PDF, WebP, JPG, PNG)',
          'Camera Document Scanner with live reticle, Magic Enhance, B&W Doc & Color filters',
          'Single combined PDF vs Individual separate PDFs mode',
          'Target file size compression presets (100KB, 200KB, 500KB)',
          'Custom text watermark engine with diagonal angle',
          'Native Android Share Sheet integration',
          'PDF page extractor to high-res JPG/PNG images',
          'Floating bubble assistive overlay',
          'Android 14 Jetpack Compose design'
        ],
        updatedAt: new Date().toISOString()
      },
      null,
      2
    )
  );

  // If dist exists, package web assets into assets/www
  const distPath = path.resolve('dist');
  if (fs.existsSync(distPath)) {
    function addDirToZip(currentPath, zipFolder) {
      const entries = fs.readdirSync(currentPath, { withFileTypes: true });
      for (const entry of entries) {
        const fullPath = path.join(currentPath, entry.name);
        if (entry.isDirectory()) {
          addDirToZip(fullPath, zipFolder.folder(entry.name));
        } else if (!entry.name.endsWith('.apk') && !entry.name.endsWith('.zip')) {
          zipFolder.file(entry.name, fs.readFileSync(fullPath));
        }
      }
    }
    addDirToZip(distPath, zip.folder('assets/www'));
  }

  const buffer = await zip.generateAsync({
    type: 'nodebuffer',
    compression: 'DEFLATE',
    compressionOptions: { level: 6 }
  });

  const outPathPublic = path.resolve('public', 'Donut Convert.apk');
  fs.writeFileSync(outPathPublic, buffer);
  fs.writeFileSync(path.resolve('public', 'DonutConvert.apk'), buffer);
  console.log(`Generated APK successfully at ${outPathPublic} (${buffer.length} bytes)`);

  if (fs.existsSync(distPath)) {
    fs.writeFileSync(path.resolve('dist', 'Donut Convert.apk'), buffer);
    fs.writeFileSync(path.resolve('dist', 'DonutConvert.apk'), buffer);
    console.log(`Synced APK to dist/`);
  }
}

generateApk();
