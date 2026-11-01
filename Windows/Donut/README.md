# Donut Convert for Windows

Donut Convert is a lightweight Windows desktop file-conversion utility that runs in
the notification area and accepts multiple files by drag and drop.

The Windows app and installer use the same sharp orange ring mark: the app
renders it as a vector, while the installer ships matching ICO and wizard
artwork so it stays clear in Explorer, setup pages, shortcuts, and the tray.

## Requirements

- Windows 10 version 1809 (build 17763) or later
- .NET 8 SDK
- Visual Studio 2022 or VS Code with the C# Dev Kit

## Run

```powershell
cd Donut
dotnet run
```

## Install for another person

Use the installer matching the device architecture. The release build
supports `win-x64` for Intel/AMD 64-bit Windows, `win-arm64` for Windows on
ARM, and `win-x86` for 32-bit Windows. Each installer is self-contained and
does not require the .NET runtime or optional Windows image codecs to be
installed. It requests administrator permission, installs Donut Convert under
`Program Files\Donut Convert`, creates Start Menu and optional Desktop shortcuts, and
adds a normal Windows uninstaller.

To build an installer, run
`.\build-release.ps1 -Version 1.3.1 -Runtime win-x64` (change the runtime for
the target device). For public distribution, sign the installer with a
trusted Windows code-signing certificate:
`$env:DONUT_SIGNING_CERTIFICATE_THUMBPRINT = "<certificate thumbprint>"`
before running the build. An unsigned installer can be blocked by Windows
Smart App Control even when the application itself is safe. The same command
can be run once for each runtime before publishing the installers.

The installer uses fast ZIP extraction to reduce installation time. This makes
the download a little larger than solid compression, but avoids spending as
long unpacking the self-contained runtime.

On first launch, choose a global keyboard shortcut. Donut then starts quietly
with Windows. Press that shortcut to open the drop window, choose a format,
and drag one or more image files onto it. The tray menu can be used to change
the shortcut later.
Converted files are written beside the originals. Existing files are never
overwritten; a numeric suffix is added instead.

## Quality behavior

If the source extension already matches the selected format, Donut copies the
file byte-for-byte. JPEG output uses the highest available GDI+ quality
setting. PNG and TIFF output use their lossless built-in codecs.

PDF output is written by the bundled ImageMagick engine. When multiple images
are dropped, Donut asks whether to merge them into one multi-page PDF or create
a separate PDF for each image.

WEBP output uses the bundled ImageMagick codec. HEIC output uses the bundled
libheif HEVC encoder, so it does not rely on optional Windows codec
registrations. The current HEIC native encoder package targets x64 Windows.
