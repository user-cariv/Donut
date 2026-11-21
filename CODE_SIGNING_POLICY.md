# Code Signing Policy

This project names **SignPath** for Microsoft code signing. SignPath Foundation
signing configuration and certificates remain controlled by SignPath and are
never stored in this repository.

## Roles

- **Committer:** `user-cariv` (me) creates and reviews source changes and
  proposes releases.
- **Reviewer:** A maintainer who reviews source changes and release artifacts
  before approval.
- **Approver:** An authorized SignPath approver manually approves every signing
  request. MFA is required for GitHub and SignPath accounts.

Signing requests are created only by GitHub-hosted Actions builds triggered for
tagged releases. Build artifacts are reproducibly assembled from the tagged
source, signed for the Windows release, and signed again after the installer is
built. Reviewers must verify hashes and contents before approval.

## Privacy

The privacy statement below describes the apps' observed behavior:

- **Donut Convert for Windows/macOS/Android:** Conversion happens locally.
  The Android app loads bundled web assets and requests camera, media, network,
  and file access for conversion features. It does not intentionally upload
  converted files to a Donut server.
- **Donut Island:** Runs locally on macOS. It reads notification and calendar
  information and requests Accessibility and Calendar permissions to display
  island/menu-bar information. It does not intentionally transmit that data.
- **Donut Boost:** Uses `HttpClient`, raw TCP sockets, and a local loopback
  HTTP/HTTPS proxy to bond configured network adapters and accelerate downloads.
  It downloads the user-selected URL, binds traffic to selected adapters, stores
  license/trial state locally, and identifies the local PC for bound licenses.
  It opens the Donut website and does not intentionally collect analytics.
- The website is hosted by GitHub Pages and GitHub may process ordinary request
  logs. Downloaded release assets are hosted by GitHub Releases.

This document describes the intended repository behavior and "needs checking"
against future releases. SignPath application status may change after account
approval.
