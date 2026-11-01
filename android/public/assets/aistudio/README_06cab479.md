# Donut Convert — Android

Kotlin + Jetpack Compose. Min SDK 29 (Android 10), target 36 (Android 16).

## Open it
1. Android Studio (Meerkat or newer) → **Open** → select this `DonutConvert` folder.
2. Install Android SDK Platform 36, then let Gradle sync (internet needed once).
3. Press **Run** on a phone/emulator.

If Studio offers to upgrade AGP / Kotlin / Compose versions, accepting is fine.
Change `applicationId` in `app/build.gradle.kts` (and the `com.donut.convert` package if you wish) before publishing.

## What's inside

| Feature | Where |
|---|---|
| Gallery with numbered multi-select (tap order = page order) | `ui/MainScreen.kt`, `MainViewModel.kt` |
| Radial donut picker (tap, or press-slide-release, with haptics) | `ui/RadialPicker.kt` |
| One PDF vs Separate PDFs sheet, reorder, page size, quality, file name, "remember my choice" | `ui/PdfOptionsSheet.kt`, `Prefs.kt` |
| PDF engine (streams page by page, no library, memory-safe) | `converter/PdfBuilder.kt` |
| JPG / PNG / WEBP conversion (HEIC and others as input) | `converter/ImageConverter.kt`, `Bitmaps.kt` |
| Safe saving, never overwrites | `converter/Saver.kt` |
| Share-sheet target ("Share → Donut") | `QuickConvertActivity.kt` + manifest |
| Floating bubble over any app | `system/FloatingBubbleService.kt` |
| Quick Settings tile | `system/DonutTileService.kt` |

## Where files are saved
- Images → `Pictures/Donut`
- PDFs → `Downloads/Donut`

## Known limits
- Android cannot natively *encode* HEIC or TIFF, so output formats are PDF, JPG, PNG, WEBP. HEIC works as **input**.
- Not included yet: long-press-anywhere overlay (Accessibility Service), auto-convert rules, voice command.
- The bubble needs "Display over other apps"; the app asks the first time you tap **Bubble**.
- Written without a device build, so expect to fix a small compile error or two on first sync.
