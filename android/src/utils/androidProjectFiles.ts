import JSZip from 'jszip';

export interface AndroidFile {
  path: string;
  name: string;
  language: string;
  description: string;
  content: string;
}

export const ANDROID_PROJECT_FILES: AndroidFile[] = [
  {
    path: 'app/src/main/java/com/donut/convert/ui/RadialPicker.kt',
    name: 'RadialPicker.kt',
    language: 'kotlin',
    description: 'Circular donut dial with angle touch tracking, haptic feedback, and sector selection',
    content: `package com.donut.convert.ui

import android.view.HapticFeedbackConstants
import androidx.compose.animation.core.animateFloatAsState
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.gestures.detectDragGestures
import androidx.compose.foundation.gestures.detectTapGestures
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.StrokeCap
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.input.pointer.pointerInput
import androidx.compose.ui.platform.LocalView
import androidx.compose.ui.unit.dp
import kotlin.math.atan2
import kotlin.math.cos
import kotlin.math.sin

enum class OutputFormat(val label: String, val color: Color) {
    PDF("PDF", Color(0xFFF59E0B)),
    JPG("JPG", Color(0xFF10B981)),
    PNG("PNG", Color(0xFF8B5CF6)),
    WEBP("WEBP", Color(0xFF0EA5E9))
}

@Composable
fun RadialDonutPicker(
    selectedCount: Int,
    onFormatSelected: (OutputFormat) -> Unit,
    modifier: Modifier = Modifier
) {
    var activeSector by remember { mutableStateOf(OutputFormat.PDF) }
    val view = LocalView.current

    Box(
        modifier = modifier.size(280.dp),
        contentAlignment = Alignment.Center
    ) {
        Canvas(
            modifier = Modifier
                .fillMaxSize()
                .pointerInput(Unit) {
                    detectTapGestures { offset ->
                        val center = Offset(size.width / 2f, size.height / 2f)
                        val angle = (atan2(offset.y - center.y, offset.x - center.x) * 180 / Math.PI + 360) % 360
                        val format = when (angle) {
                            in 315.0..360.0, in 0.0..45.0 -> OutputFormat.PDF
                            in 45.0..135.0 -> OutputFormat.JPG
                            in 135.0..225.0 -> OutputFormat.PNG
                            else -> OutputFormat.WEBP
                        }
                        activeSector = format
                        view.performHapticFeedback(HapticFeedbackConstants.KEYBOARD_TAP)
                        onFormatSelected(format)
                    }
                }
        ) {
            val strokeW = 48.dp.toPx()
            val diameter = size.minDimension - strokeW
            val arcSize = Size(diameter, diameter)
            val topLeft = Offset((size.width - diameter) / 2f, (size.height - diameter) / 2f)

            // Draw donut sectors
            val formats = listOf(
                OutputFormat.PDF to -45f,
                OutputFormat.JPG to 45f,
                OutputFormat.PNG to 135f,
                OutputFormat.WEBP to 225f
            )

            formats.forEach { (fmt, startAngle) ->
                val isSelected = activeSector == fmt
                drawArc(
                    color = if (isSelected) fmt.color else Color(0xFF262422),
                    startAngle = startAngle + 4f,
                    sweepAngle = 82f,
                    useCenter = false,
                    topLeft = topLeft,
                    size = arcSize,
                    style = Stroke(width = if (isSelected) strokeW * 1.1f else strokeW)
                )
            }
        }

        // Center Action Hub
        Surface(
            shape = MaterialTheme.shapes.extraLarge,
            color = MaterialTheme.colorScheme.surfaceVariant,
            modifier = Modifier.size(110.dp),
            onClick = {
                view.performHapticFeedback(HapticFeedbackConstants.CONFIRM)
                onFormatSelected(activeSector)
            }
        ) {
            Column(
                horizontalAlignment = Alignment.CenterHorizontally,
                verticalArrangement = Arrangement.Center
            ) {
                Text("CONVERT", style = MaterialTheme.typography.labelSmall, color = activeSector.color)
                Text(activeSector.label, style = MaterialTheme.typography.titleLarge)
                Text("$selectedCount items", style = MaterialTheme.typography.bodySmall)
            }
        }
    }
}`
  },
  {
    path: 'app/src/main/java/com/donut/convert/converter/PdfBuilder.kt',
    name: 'PdfBuilder.kt',
    language: 'kotlin',
    description: 'Memory-safe streaming PDF engine without external bloat libraries',
    content: `package com.donut.convert.converter

import android.content.Context
import android.graphics.Bitmap
import android.graphics.pdf.PdfDocument
import android.net.Uri
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.io.File
import java.io.FileOutputStream

object PdfBuilder {
    suspend fun createPdf(
        context: Context,
        imageUris: List<Uri>,
        outputFile: File,
        pageSize: PageSizeOption = PageSizeOption.A4,
        quality: Int = 85,
        onProgress: (current: Int, total: Int) -> Unit
    ): Result<File> = withContext(Dispatchers.IO) {
        val document = PdfDocument()
        try {
            imageUris.forEachIndexed { index, uri ->
                onProgress(index + 1, imageUris.size)
                
                // Stream bitmap sample without blowing memory
                val bitmap = Bitmaps.decodeSampledBitmapFromUri(context, uri, 1800, 2400)
                if (bitmap != null) {
                    val pageInfo = PdfDocument.PageInfo.Builder(
                        bitmap.width,
                        bitmap.height,
                        index + 1
                    ).create()

                    val page = document.startPage(pageInfo)
                    page.canvas.drawBitmap(bitmap, 0f, 0f, null)
                    document.finishPage(page)
                    bitmap.recycle()
                }
            }

            FileOutputStream(outputFile).use { out ->
                document.writeTo(out)
            }
            Result.success(outputFile)
        } catch (e: Exception) {
            Result.failure(e)
        } finally {
            document.close()
        }
    }
}

enum class PageSizeOption { A4, US_LETTER, ORIGINAL, FIT }`
  },
  {
    path: 'app/src/main/java/com/donut/convert/system/FloatingBubbleService.kt',
    name: 'FloatingBubbleService.kt',
    language: 'kotlin',
    description: 'Android floating bubble service overlaying over any other app for instant conversion',
    content: `package com.donut.convert.system

import android.app.Service
import android.content.Intent
import android.graphics.PixelFormat
import android.os.IBinder
import android.view.Gravity
import android.view.LayoutInflater
import android.view.MotionEvent
import android.view.View
import android.view.WindowManager
import com.donut.convert.R

class FloatingBubbleService : Service() {
    private var windowManager: WindowManager? = null
    private var floatingView: View? = null

    override fun onBind(intent: Intent?): IBinder? = null

    override fun onCreate() {
        super.onCreate()
        windowManager = getSystemService(WINDOW_SERVICE) as WindowManager
        floatingView = LayoutInflater.from(this).inflate(R.layout.layout_floating_bubble, null)

        val params = WindowManager.LayoutParams(
            WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.WRAP_CONTENT,
            WindowManager.LayoutParams.TYPE_APPLICATION_OVERLAY,
            WindowManager.LayoutParams.FLAG_NOT_FOCUSABLE,
            PixelFormat.TRANSLUCENT
        ).apply {
            gravity = Gravity.TOP or Gravity.START
            x = 100
            y = 200
        }

        windowManager?.addView(floatingView, params)
        setupDragListener(params)
    }

    private fun setupDragListener(params: WindowManager.LayoutParams) {
        var initialX = 0
        var initialY = 0
        var initialTouchX = 0f
        var initialTouchY = 0f

        floatingView?.setOnTouchListener { v, event ->
            when (event.action) {
                MotionEvent.ACTION_DOWN -> {
                    initialX = params.x
                    initialY = params.y
                    initialTouchX = event.rawX
                    initialTouchY = event.rawY
                    true
                }
                MotionEvent.ACTION_MOVE -> {
                    params.x = initialX + (event.rawX - initialTouchX).toInt()
                    params.y = initialY + (event.rawY - initialTouchY).toInt()
                    windowManager?.updateViewLayout(floatingView, params)
                    true
                }
                else -> false
            }
        }
    }

    override fun onDestroy() {
        super.onDestroy()
        if (floatingView != null) windowManager?.removeView(floatingView)
    }
}`
  },
  {
    path: 'app/build.gradle.kts',
    name: 'app/build.gradle.kts',
    language: 'kotlin',
    description: 'App module build configuration for Jetpack Compose, Min SDK 36, Target SDK 36',
    content: `plugins {
    id("com.android.application")
    id("org.jetbrains.kotlin.android")
}

android {
    namespace = "com.donut.convert"
    compileSdk = 36

    defaultConfig {
        applicationId = "com.donut.convert"
        minSdk = 36
        targetSdk = 36
        versionCode = 3
        versionName = "1.3.0"

        testInstrumentationRunner = "androidx.test.runner.AndroidJUnitRunner"
        vectorDrawables {
            useSupportLibrary = true
        }
    }

    buildFeatures {
        compose = true
    }
    composeOptions {
        kotlinCompilerExtensionVersion = "1.5.14"
    }
    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }
    kotlinOptions {
        jvmTarget = "17"
    }
}

dependencies {
    implementation("androidx.core:core-ktx:1.13.1")
    implementation("androidx.lifecycle:lifecycle-runtime-ktx:2.8.4")
    implementation("androidx.activity:activity-compose:1.9.1")
    implementation(platform("androidx.compose:compose-bom:2024.06.00"))
    implementation("androidx.compose.ui:ui")
    implementation("androidx.compose.ui:ui-graphics")
    implementation("androidx.compose.ui:ui-tooling-preview")
    implementation("androidx.compose.material3:material3")
    implementation("io.coil-kt:coil-compose:2.7.0")
    implementation("androidx.documentfile:documentfile:1.0.1")
}`
  },
  {
    path: 'app/src/main/AndroidManifest.xml',
    name: 'AndroidManifest.xml',
    language: 'xml',
    description: 'Manifest with QuickConvertActivity share target and floating bubble permission',
    content: `<?xml version="1.0" encoding="utf-8"?>
<manifest xmlns:android="http://schemas.android.com/apk/res/android">

    <uses-permission android:name="android.permission.READ_MEDIA_IMAGES" />
    <uses-permission android:name="android.permission.SYSTEM_ALERT_WINDOW" />
    <uses-permission android:name="android.permission.VIBRATE" />

    <application
        android:allowBackup="true"
        android:icon="@mipmap/ic_launcher"
        android:label="Donut Convert"
        android:roundIcon="@mipmap/ic_launcher_round"
        android:supportsRtl="true"
        android:theme="@style/Theme.DonutConvert">

        <activity
            android:name=".MainActivity"
            android:exported="true"
            android:theme="@style/Theme.DonutConvert">
            <intent-filter>
                <action android:name="android.intent.action.MAIN" />
                <category android:name="android.intent.category.LAUNCHER" />
            </intent-filter>
        </activity>

        <!-- Quick Convert Share-Sheet Receiver -->
        <activity
            android:name=".QuickConvertActivity"
            android:exported="true"
            android:theme="@style/Theme.DonutConvert.Dialog">
            <intent-filter>
                <action android:name="android.intent.action.SEND" />
                <action android:name="android.intent.action.SEND_MULTIPLE" />
                <category android:name="android.intent.category.DEFAULT" />
                <data android:mimeType="image/*" />
            </intent-filter>
        </activity>

        <service
            android:name=".system.FloatingBubbleService"
            android:enabled="true"
            android:exported="false" />

    </application>
</manifest>`
  }
];

export async function downloadAndroidProjectZip(): Promise<Blob> {
  const zip = new JSZip();

  // Root files
  zip.file(
    'README.md',
    `# Donut Convert — Android Studio Project
Kotlin + Jetpack Compose. Min SDK 29, target 36 (Android 16+).

Desktop & Web version available at: https://user-cariv.github.io/Donut-Convert/

## Open in Android Studio
1. Open Android Studio (Meerkat or newer)
2. Open this folder.
3. Gradle sync will complete automatically.
4. Press Run on device/emulator!`
  );

  zip.file(
    'build.gradle.kts',
    `plugins {
    id("com.android.application") version "8.9.1" apply false
    id("org.jetbrains.kotlin.android") version "1.9.24" apply false
}`
  );

  zip.file(
    'settings.gradle.kts',
    `pluginManagement {
    repositories { google(); mavenCentral(); gradlePluginPortal() }
}
dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories { google(); mavenCentral() }
}
rootProject.name = "DonutConvert"
include(":app")`
  );

  zip.file(
    'gradle.properties',
    `org.gradle.jvmargs=-Xmx2048m -Dfile.encoding=UTF-8
android.useAndroidX=true
android.nonTransitiveRClass=true
kotlin.code.style=official`
  );

  // Add individual Android source files
  ANDROID_PROJECT_FILES.forEach((f) => {
    zip.file(f.path, f.content);
  });

  return await zip.generateAsync({ type: 'blob', compression: 'DEFLATE' });
}
