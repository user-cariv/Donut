import AppKit
import SwiftUI
import ServiceManagement

/// Borderless panel that floats over the menu bar / notch and never steals focus.
final class IslandPanel: NSPanel {
    override var canBecomeKey: Bool { false }
    override var canBecomeMain: Bool { false }

    // Stop AppKit from pushing the panel below the menu bar.
    override func constrainFrameRect(_ frameRect: NSRect, to screen: NSScreen?) -> NSRect {
        return frameRect
    }
}

final class FirstMouseHostingView<Content: View>: NSHostingView<Content> {
    override func acceptsFirstMouse(for event: NSEvent?) -> Bool { true }
}

final class AppDelegate: NSObject, NSApplicationDelegate, NSMenuDelegate {
    let model = IslandModel()
    let monitor = NotificationMonitor()
    lazy var calendar = CalendarManager(model: model)

    private var panel: IslandPanel!
    private var statusItem: NSStatusItem!
    private var hoverItem: NSMenuItem!
    private var loginItem: NSMenuItem!
    private var glassItem: NSMenuItem!
    private var translucencyItems: [NSMenuItem] = []
    private var globalMouse: Any?
    private var localMouse: Any?
    private var mouseInside = false
    private var trustTimer: Timer?

    // MARK: Lifecycle

    func applicationDidFinishLaunching(_ notification: Notification) {
        NSApp.setActivationPolicy(.accessory)

        refreshNotch()
        buildPanel()
        buildStatusItem()

        model.requestAccessibility = { [weak self] in self?.askAccessibility() }
        model.requestCalendar = { [weak self] in self?.calendar.requestAccess() }

        monitor.onNotice = { [weak self] n in self?.model.present(n) }
        monitor.onGone = { [weak self] sig in self?.model.noticeGone(signature: sig) }

        model.accessibilityTrusted = NotificationMonitor.isTrusted
        if !NotificationMonitor.isTrusted { NotificationMonitor.promptForTrust() }
        trustTimer = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { [weak self] _ in
            self?.model.accessibilityTrusted = NotificationMonitor.isTrusted
        }

        monitor.start()
        calendar.start()
        installMouseMonitors()

        NotificationCenter.default.addObserver(self,
                                               selector: #selector(screensChanged),
                                               name: NSApplication.didChangeScreenParametersNotification,
                                               object: nil)
    }

    // MARK: Screen / panel

    private func notchScreen() -> NSScreen {
        return NSScreen.screens.first(where: { $0.safeAreaInsets.top > 0 })
            ?? NSScreen.main
            ?? NSScreen.screens[0]
    }

    private func refreshNotch() {
        let s = notchScreen()
        if s.safeAreaInsets.top > 0,
           let left = s.auxiliaryTopLeftArea,
           let right = s.auxiliaryTopRightArea {
            model.notch = CGSize(width: s.frame.width - left.width - right.width,
                                 height: s.safeAreaInsets.top)
        } else {
            // Macs without a notch (e.g. 13" MacBook Pro): draw a virtual pill in the menu bar.
            let menuBar = max(24, s.frame.maxY - s.visibleFrame.maxY)
            model.notch = CGSize(width: 190, height: menuBar)
        }
    }

    private func buildPanel() {
        let p = IslandPanel(contentRect: .zero,
                            styleMask: [.borderless, .nonactivatingPanel],
                            backing: .buffered,
                            defer: false)
        p.isOpaque = false
        p.backgroundColor = .clear
        p.hasShadow = false
        p.level = NSWindow.Level(rawValue: NSWindow.Level.mainMenu.rawValue + 3)
        p.collectionBehavior = [.canJoinAllSpaces, .stationary, .fullScreenAuxiliary, .ignoresCycle]
        p.isMovable = false
        p.hidesOnDeactivate = false
        p.isReleasedWhenClosed = false
        p.contentView = FirstMouseHostingView(rootView: IslandView(model: model))
        panel = p

        model.layoutHandler = { [weak self] size in self?.layoutPanel(contentSize: size) }
        layoutPanel(contentSize: model.size)
        p.orderFrontRegardless()
    }

    private func layoutPanel(contentSize: CGSize) {
        let s = notchScreen()
        let padX: CGFloat = 40   // room for the shadow and the spring overshoot
        let padBottom: CGFloat = 48
        let w = contentSize.width + padX * 2
        let h = contentSize.height + padBottom
        let frame = NSRect(x: s.frame.midX - w / 2, y: s.frame.maxY - h, width: w, height: h)
        panel.setFrame(frame, display: true)
    }

    @objc private func screensChanged() {
        refreshNotch()
        layoutPanel(contentSize: model.size)
    }

    // MARK: Hover (global so it works even though the collapsed panel is tiny)

    private func installMouseMonitors() {
        globalMouse = NSEvent.addGlobalMonitorForEvents(matching: [.mouseMoved]) { [weak self] _ in
            self?.updateHover()
        }
        localMouse = NSEvent.addLocalMonitorForEvents(matching: [.mouseMoved]) { [weak self] event in
            self?.updateHover()
            return event
        }
    }

    private func updateHover() {
        let s = notchScreen()
        let size = model.size
        let rect = NSRect(x: s.frame.midX - size.width / 2 - 8,
                          y: s.frame.maxY - size.height - 8,
                          width: size.width + 16,
                          height: size.height + 16)
        let inside = rect.contains(NSEvent.mouseLocation)
        if inside != mouseInside {
            mouseInside = inside
            model.hoverChanged(inside)
        }
    }

    // MARK: Menu bar item

    private func buildStatusItem() {
        statusItem = NSStatusBar.system.statusItem(withLength: NSStatusItem.squareLength)
        if let button = statusItem.button {
            button.title = "🍩"
        }

        let menu = NSMenu()
        menu.delegate = self

        menu.addItem(item("Open Island", #selector(openIsland)))
        menu.addItem(.separator())

        let tests = NSMenu()
        tests.addItem(item("Message", #selector(testMessage)))
        tests.addItem(item("WhatsApp", #selector(testWhatsApp)))
        tests.addItem(item("Email", #selector(testEmail)))
        tests.addItem(item("Incoming call", #selector(testCall)))
        tests.addItem(item("Calendar event", #selector(testEvent)))
        let testsItem = NSMenuItem(title: "Preview", action: nil, keyEquivalent: "")
        testsItem.submenu = tests
        menu.addItem(testsItem)
        menu.addItem(.separator())

        glassItem = item("Liquid Glass", #selector(toggleGlass))
        menu.addItem(glassItem)

        let transMenu = NSMenu()
        for (title, pct) in [("Solid", 0), ("Light", 35), ("Medium", 60), ("Clear", 85)] {
            let it = item(title, #selector(setTranslucency(_:)))
            it.tag = pct
            transMenu.addItem(it)
            translucencyItems.append(it)
        }
        let transItem = NSMenuItem(title: "Translucency", action: nil, keyEquivalent: "")
        transItem.submenu = transMenu
        menu.addItem(transItem)

        hoverItem = item("Expand on hover", #selector(toggleHover))
        menu.addItem(hoverItem)
        loginItem = item("Launch at login", #selector(toggleLogin))
        menu.addItem(loginItem)
        menu.addItem(.separator())

        menu.addItem(item("Grant Accessibility access…", #selector(askAccessibility)))
        menu.addItem(item("Connect Calendar…", #selector(askCalendar)))
        menu.addItem(.separator())
        menu.addItem(item("Quit Donut Island", #selector(quit)))

        statusItem.menu = menu
    }

    private func item(_ title: String, _ action: Selector) -> NSMenuItem {
        let i = NSMenuItem(title: title, action: action, keyEquivalent: "")
        i.target = self
        return i
    }

    func menuNeedsUpdate(_ menu: NSMenu) {
        hoverItem?.state = model.expandOnHover ? .on : .off
        loginItem?.state = (SMAppService.mainApp.status == .enabled) ? .on : .off
        glassItem?.state = model.liquidGlass ? .on : .off
        for it in translucencyItems {
            it.state = abs(model.translucency - Double(it.tag) / 100.0) < 0.08 ? .on : .off
        }
    }

    // MARK: Menu actions

    @objc private func openIsland() { model.toggleExpanded() }

    @objc private func toggleHover() { model.expandOnHover.toggle() }

    @objc private func toggleGlass() {
        model.liquidGlass.toggle()
        model.openForPreview()
    }

    @objc private func setTranslucency(_ sender: NSMenuItem) {
        model.translucency = Double(sender.tag) / 100.0
        model.openForPreview()
    }

    @objc private func toggleLogin() {
        do {
            if SMAppService.mainApp.status == .enabled {
                try SMAppService.mainApp.unregister()
            } else {
                try SMAppService.mainApp.register()
            }
        } catch {
            NSLog("DonutIsland: launch-at-login failed: \(error)")
        }
    }

    @objc private func askAccessibility() {
        NotificationMonitor.promptForTrust()
        if let url = URL(string: "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility") {
            NSWorkspace.shared.open(url)
        }
    }

    @objc private func askCalendar() { calendar.requestAccess() }

    @objc private func quit() { NSApp.terminate(nil) }

    // MARK: Previews

    @objc private func testMessage() {
        model.present(IslandNotice(kind: .sms, appName: "Messages", title: "Mom",
                                   body: "Dinner at 8? I made your favourite 🍝"))
    }

    @objc private func testWhatsApp() {
        model.present(IslandNotice(kind: .whatsapp, appName: "WhatsApp", title: "Group 09 – AI & Communication",
                                   body: "Rohan: slides are ready, can everyone check?"))
    }

    @objc private func testEmail() {
        model.present(IslandNotice(kind: .email, appName: "Mail", title: "Prof. Mehta",
                                   body: "Presentation schedule for next week"))
    }

    @objc private func testCall() {
        model.present(IslandNotice(kind: .call, appName: "Phone", title: "Aarav Sharma",
                                   body: "Incoming call · iPhone",
                                   signature: "test-call",
                                   onAccept: { NSLog("DonutIsland: test call accepted") },
                                   onDecline: { NSLog("DonutIsland: test call declined") }))
    }

    @objc private func testEvent() {
        model.present(IslandNotice(kind: .calendar, appName: "Calendar", title: "Project review",
                                   body: "Starts in 10 min · Room 4"))
    }
}
