# Donut Island

A Dynamic Island for notched MacBook Pros (M1 Pro/Max through M5). It lives over the notch, morphs with spring animations, and shows:

- **Calendar**: Apple, Google and anything else in macOS Calendar (via EventKit)
- **Messages**: SMS and iMessage
- **WhatsApp**: via WhatsApp Desktop
- **Email**: Mail, Gmail and Outlook notifications
- **Incoming calls**: iPhone calls via Continuity, and FaceTime, with Accept / Decline buttons

On Macs without a notch, it draws a virtual pill in the menu bar.

## Build and run

1. Unzip the folder.
2. Open Terminal in the folder and run:

```bash
xattr -cr .          # removes the "downloaded from the internet" flag
chmod +x build.sh
./build.sh --install
```

If it says Swift is missing, run `xcode-select --install` once (about 1 GB), then run the build again.
You can also open `Package.swift` in Xcode and press Run.

Look for the 🍩 in the menu bar. **Preview** in that menu lets you test every kind of notification without waiting for a real one.

## Permissions (one-time)

| Permission | Why | Where |
|---|---|---|
| **Accessibility** | Reads the banners macOS shows for messages, emails and calls | System Settings > Privacy & Security > Accessibility > enable Donut Island |
| **Calendars** | Shows your events | Prompted on first launch; or menu > Connect Calendar… |

The app is ad-hoc signed, so macOS may ask you to re-grant Accessibility after you rebuild it. Remove Donut Island from the list with the minus button and add it again if it stops working.

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
- **Liquid Glass on** adds a glass rim, a light sheen and, on macOS 26 or later, Apple's own glass material. Off gives plain frosted translucency.
- Real Apple glass is only compiled in when you build with Xcode 26 or newer (`xcode-select --install` after updating macOS gets you that). On older versions you still get the blur, rim and sheen.
