import AppKit
import ApplicationServices

// MARK: - Small Accessibility helpers

enum AX {
    struct Harvest {
        var texts: [String] = []
        var all: [String] = []
        var buttons: [(title: String, el: AXUIElement)] = []
        var visited = 0
    }

    static func copy(_ el: AXUIElement, _ name: String) -> CFTypeRef? {
        var value: CFTypeRef?
        guard AXUIElementCopyAttributeValue(el, name as CFString, &value) == .success else { return nil }
        return value
    }

    static func string(_ el: AXUIElement, _ name: String) -> String? {
        return copy(el, name) as? String
    }

    static func elements(_ el: AXUIElement, _ name: String) -> [AXUIElement] {
        return (copy(el, name) as? [AXUIElement]) ?? []
    }

    static func size(_ el: AXUIElement) -> CGSize? {
        guard let v = copy(el, kAXSizeAttribute), CFGetTypeID(v) == AXValueGetTypeID() else { return nil }
        var s = CGSize.zero
        guard AXValueGetValue(v as! AXValue, .cgSize, &s) else { return nil }
        return s
    }

    static func press(_ el: AXUIElement) {
        _ = AXUIElementPerformAction(el, kAXPressAction as CFString)
    }

    static func harvest(_ el: AXUIElement, depth: Int, into h: inout Harvest) {
        h.visited += 1
        if depth > 10 || h.visited > 250 { return }

        let role = string(el, kAXRoleAttribute) ?? ""
        for name in [kAXTitleAttribute, kAXDescriptionAttribute, kAXHelpAttribute, kAXIdentifierAttribute] {
            if let s = string(el, name), !s.isEmpty { h.all.append(s) }
        }
        if role == kAXStaticTextRole {
            if let v = string(el, kAXValueAttribute) ?? string(el, kAXTitleAttribute), !v.isEmpty {
                h.texts.append(v)
            }
        }
        if role == kAXButtonRole {
            let t = string(el, kAXTitleAttribute) ?? string(el, kAXDescriptionAttribute) ?? ""
            h.buttons.append((title: t, el: el))
        }
        for child in elements(el, kAXChildrenAttribute) {
            harvest(child, depth: depth + 1, into: &h)
        }
    }
}

/// Wraps an AXUIElement so closures can capture it.
final class AXBox: @unchecked Sendable {
    let el: AXUIElement
    init(_ el: AXUIElement) { self.el = el }
}

// MARK: - Monitor

/// macOS (and iPhone Continuity) shows SMS, iMessage, WhatsApp, Mail and incoming-call
/// banners through the NotificationCenter process. We read those banners with the
/// Accessibility API, which is what other notch apps do too.
final class NotificationMonitor {
    var onNotice: ((IslandNotice) -> Void)?
    var onGone: ((String) -> Void)?

    private let queue = DispatchQueue(label: "donutisland.notification-monitor", qos: .userInitiated)
    private var timer: DispatchSourceTimer?
    private var active = Set<String>()
    private var primed = false

    static var isTrusted: Bool { AXIsProcessTrusted() }

    static func promptForTrust() {
        let options = ["AXTrustedCheckOptionPrompt": true] as CFDictionary
        _ = AXIsProcessTrustedWithOptions(options)
    }

    func start() {
        let t = DispatchSource.makeTimerSource(queue: queue)
        t.schedule(deadline: .now() + 1, repeating: .milliseconds(350))
        t.setEventHandler { [weak self] in self?.poll() }
        t.resume()
        timer = t
    }

    private func poll() {
        guard AXIsProcessTrusted() else { return }
        guard let pid = NSRunningApplication
            .runningApplications(withBundleIdentifier: "com.apple.notificationcenterui")
            .first?.processIdentifier else { return }

        let app = AXUIElementCreateApplication(pid)
        AXUIElementSetMessagingTimeout(app, 0.5)

        var current: [String: IslandNotice] = [:]
        var bannerCount = 0
        for window in AX.elements(app, kAXWindowsAttribute) {
            for banner in bannerElements(in: window) {
                bannerCount += 1
                if let n = parse(banner) { current[n.signature] = n }
            }
        }

        let keys = Set(current.keys)
        let fresh = keys.subtracting(active)
        let gone = active.subtracting(keys)
        let wasPrimed = primed
        active = keys
        primed = true

        // Ignore the first scan (anything already on screen at launch) and the big
        // Notification Center panel (many banners at once, with relative timestamps).
        if !wasPrimed || bannerCount > 3 { return }

        for sig in fresh {
            if let n = current[sig] {
                DispatchQueue.main.async { [weak self] in self?.onNotice?(n) }
            }
        }
        for sig in gone {
            DispatchQueue.main.async { [weak self] in self?.onGone?(sig) }
        }
    }

    private func bannerElements(in window: AXUIElement) -> [AXUIElement] {
        var found: [AXUIElement] = []

        func walk(_ el: AXUIElement, _ depth: Int) {
            if depth > 8 { return }
            let sub = AX.string(el, kAXSubroleAttribute) ?? ""
            if sub.contains("NotificationCenter") && !sub.contains("Stack") {
                found.append(el)
                return
            }
            for child in AX.elements(el, kAXChildrenAttribute) {
                walk(child, depth + 1)
            }
        }

        walk(window, 0)

        // Fallback for macOS versions that don't tag banners with a subrole.
        if found.isEmpty, let s = AX.size(window), s.width <= 480, s.height <= 260 {
            found = [window]
        }
        return found
    }

    private func parse(_ el: AXUIElement) -> IslandNotice? {
        var h = AX.Harvest()
        AX.harvest(el, depth: 0, into: &h)

        let texts = h.texts
            .map { $0.trimmingCharacters(in: .whitespacesAndNewlines) }
            .filter { !$0.isEmpty }
        guard !texts.isEmpty else { return nil }

        // Only short attribute strings (app names, identifiers) are used to classify,
        // so a message body that happens to say "mail" can't change the kind.
        let hints = (h.all.filter { $0.count <= 40 } + texts.prefix(1))
            .joined(separator: " ")
            .lowercased()

        let acceptBtn = h.buttons.first { ["accept", "answer"].contains($0.title.lowercased()) }
        let declineBtn = h.buttons.first { ["decline", "reject"].contains($0.title.lowercased()) }

        var kind: NoticeKind = .other
        var appName = "Notification"
        if acceptBtn != nil || declineBtn != nil || hints.contains("incoming call") || hints.contains("facetime") {
            kind = .call; appName = "Phone"
        } else if hints.contains("whatsapp") {
            kind = .whatsapp; appName = "WhatsApp"
        } else if hints.contains("mail") || hints.contains("gmail") || hints.contains("outlook") || hints.contains("spark") {
            kind = .email; appName = "Mail"
        } else if hints.contains("messages") || hints.contains("imessage") || hints.contains("sms") {
            kind = .sms; appName = "Messages"
        } else if hints.contains("calendar") {
            kind = .calendar; appName = "Calendar"
        }

        var parts = texts
        let knownApps = ["messages", "whatsapp", "mail", "gmail", "facetime", "phone", "calendar", "outlook"]
        if let first = parts.first, knownApps.contains(first.lowercased()), parts.count > 1 {
            parts.removeFirst()
        }
        guard let title = parts.first else { return nil }
        var body = parts.dropFirst().joined(separator: " ")
        if kind == .call && body.isEmpty { body = "Incoming call" }

        var notice = IslandNotice(kind: kind, appName: appName, title: title, body: body)
        notice.signature = kind.rawValue + "|" + texts.joined(separator: "|")

        if kind == .call {
            if let a = acceptBtn {
                let box = AXBox(a.el)
                notice.onAccept = { AX.press(box.el) }
            }
            if let d = declineBtn {
                let box = AXBox(d.el)
                notice.onDecline = { AX.press(box.el) }
            }
        }
        return notice
    }
}
