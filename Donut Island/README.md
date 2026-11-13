# Donut Island

A Dynamic Island for notched MacBook Pros (M1 Pro/Max through M5). It lives over the notch, morphs with spring animations, and shows:

- **Calendar**: Apple, Google and anything else in macOS Calendar (via EventKit)
- **Messages**: SMS and iMessage
- **WhatsApp**: via WhatsApp Desktop
- **Email**: Mail, Gmail and Outlook notifications
- **Incoming calls**: iPhone calls via Continuity, and FaceTime, with Accept / Decline buttons

On Macs without a notch, it draws a virtual pill in the menu bar.

## Install and run

Donut Island requires macOS 27 or later on Intel or Apple silicon Macs. The notch animation works on MacBooks with or without a notch.

For a ready-to-use download, install the app bundle provided by the publisher. On first launch, macOS asks each Mac user to approve Calendar access. To show message, email, WhatsApp and call banners, each user must also enable Donut Island under **System Settings > Privacy & Security > Accessibility**. These approvals belong to the macOS user account and cannot be granted by the installer or by the app. If macOS blocks an unsigned download, use the publisher's signed and notarized release, when available.

### Build from source

1. Unzip the folder, keeping `Donut Island Sunset Oasis.png` beside `build.sh`.
2. Open Terminal in the folder and run:

```bash
xattr -cr .          # removes the "downloaded from the internet" flag
chmod +x build.sh
./build.sh --install
```

If it says Swift is missing, run `xcode-select --install` once (about 1 GB), then run the build again.
You can also open `Package.swift` in Xcode and press Run.

Look for the 🍩 in the menu bar. **Preview** in that menu lets you test every kind of notification without waiting for a real one.

## Permissions (per Mac user)

| Permission | Why | Where |
|---|---|---|
| **Accessibility** | Reads the banners macOS shows for messages, emails and calls | System Settings > Privacy & Security > Accessibility > enable Donut Island |
| **Calendars** | Shows your events | Prompted on first launch; or menu > Connect Calendar… |

macOS requires the logged-in user to grant these permissions. An administrator or installer cannot pre-approve them for every user. The source build is ad-hoc signed, so macOS may ask you to grant Accessibility again after rebuilding; remove the old Donut Island entry and add the rebuilt app if access stops working. For straightforward public downloads, distribute a Developer ID signed and notarized build.

## Getting each source to show up

- **Google Calendar**: System Settings > Internet Accounts > Add Google > turn on *Calendars*. It then appears automatically.
- **SMS on the Mac**: on iPhone, Settings > Messages > Text Message Forwarding > turn on your Mac.
- **iPhone calls on the Mac**: on iPhone, Settings > Phone > Calls on Other Devices > allow your Mac. On the Mac, FaceTime > Settings > turn on *Calls from iPhone*.
- **WhatsApp**: install WhatsApp Desktop and sign in. Notifications must be enabled for it.
- **Email**: Mail app with your Gmail account added, or any app that sends macOS notifications.
- In System Settings > Notifications, each of these apps must have *Allow Notifications* on with the **Banners** or **Alerts** style. The island reads the on-screen banner, so the style cannot be "None".

## Controls

- Hover over the notch (or use menu > Open Island) to expand it.
- Click a notification to open its app.
- Menu bar icon: toggle hover-to-expand, launch at login, permissions, quit.

## Known limits

- macOS still shows its own banner at the top right. Apple does not allow another app to hide it.
- Message, WhatsApp, email and call detection reads Apple's notification UI, so a macOS update can change how it is laid out. If a source stops appearing after an update, tell me the macOS version and what you see.
- Now Playing music is not included. Apple blocked the private API for it in macOS 15.4.
- This app can't go on the Mac App Store because of the Accessibility use.

## Appearance (Liquid Glass and translucency)

- Tap the slider icon in the expanded island to get a **Liquid Glass** switch and a **Translucency** slider. You see the change live. The menu bar 🍩 menu has the same options, with presets (Solid, Light, Medium, Clear).
- **Translucency** goes from solid black (0%) to mostly see-through (100%). The collapsed island is always solid black so it matches the physical notch.
- **Liquid Glass on** adds a glass rim, a light sheen and Apple's native glass material, styled to echo the translucent Liquid Glass look on iPhone. Off gives plain frosted translucency.
- Build with Xcode 26 or newer (`xcode-select --install`) to include Apple's native glass material. The app requires macOS 27 or later.
