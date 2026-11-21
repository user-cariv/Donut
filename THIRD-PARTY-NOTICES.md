# Third-Party Notices

This file records third-party dependencies and native components included in or
used to build the Donut applications. Dependency license text must be rechecked
before each release. MIT is the selected license for this project and is
compatible with the commonly permissive licenses listed below, but compliance
depends on the exact dependency versions and included files.

## Warning

**License information and native binaries need verification before release.**
Run a dependency-license audit for NuGet and npm, and retain the upstream
license/notice files in source or redistributable packages. Resolve any GPL,
AGPL, SSPL, proprietary, or restrictive component before redistribution.
The checked npm lockfile contains MIT, Apache-2.0, BSD, ISC, 0BSD, Blue Oak,
MPL-2.0, CC-BY-4.0, Zlib, and dual or custom license fields. The MIT choice
does not require GPL relicensing for packages that offer an MIT alternative,
but the Android build must choose the MIT side of JSZip's
`MIT OR GPL-3.0-or-later` declaration and must not incorporate the GPL option
accidentally. `rgbcolor` declares `MIT OR SEE LICENSE IN FEEL-FREE.md`; the
exact upstream terms and whether that optional package is shipped must be
verified. CC-BY-4.0 metadata should be checked to determine whether attribution
is needed beyond package source metadata. No clear GPL conflict is presently
confirmed, but this is not a final legal determination.

## Windows - Donut Convert

| Component | Version | Type | License to verify |
|---|---:|---|---|
| .NET 8 runtime | 8 | Microsoft runtime | Microsoft .NET LICENSE |
| LibHeif.Native.win-x64 | 1.15.1 | Native HEIF library | Upstream license/NOTICE |
| LibHeifSharp | 3.2.0 | .NET wrapper | Package license |
| Magick.NET-Q8-AnyCPU | 15.0.0 | ImageMagick bindings | Apache-2.0 or upstream ImageMagick terms |

Native DLLs in the published app may include libheif, ImageMagick or codec
components and their transitive dependencies. They must be inventoried after
publish and included in this notice.

## Windows - Donut Boost

No NuGet PackageReference dependencies are declared in `DonutBoost.csproj`.
It uses .NET 8 framework APIs and WPF. Verify bundled assets and any native
components before release.

## macOS

- Donut Island: Swift standard libraries and AppKit only; no Swift package
  dependencies are declared in `Package.swift`.
- Donut Convert: macOS AppKit/Foundation and native/macOS build tools. Verify
  bundled converter resources after packaging.

## Android

Direct npm dependencies declared in `android/package.json`:

`@google/genai`, `@tailwindcss/vite`, `@vitejs/plugin-react`, `canvas-confetti`,
`dotenv`, `express`, `jspdf`, `jszip`, `lucide-react`, `motion`, `pdfjs-dist`,
`qrcode`, `react`, `react-dom`, `vite`, plus TypeScript/build/dev dependencies.
Use `npm` license metadata and the package-lock license fields to complete the
upstream notices. In particular, PDF and image conversion libraries may ship
native or font assets that require separate attribution.
