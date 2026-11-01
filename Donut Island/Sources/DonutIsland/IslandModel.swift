import SwiftUI
import AppKit

// MARK: - Data types

enum NoticeKind: String {
    case sms, whatsapp, email, call, calendar, other

    var symbol: String {
        switch self {
        case .sms: return "message.fill"
        case .whatsapp: return "bubble.left.and.bubble.right.fill"
        case .email: return "envelope.fill"
        case .call: return "phone.fill"
        case .calendar: return "calendar"
        case .other: return "bell.fill"
        }
    }

    var color: Color {
        switch self {
        case .sms: return Color(red: 0.20, green: 0.78, blue: 0.35)
        case .whatsapp: return Color(red: 0.145, green: 0.827, blue: 0.40)
        case .email: return Color(red: 0.0, green: 0.48, blue: 1.0)
        case .call: return Color(red: 0.20, green: 0.78, blue: 0.35)
        case .calendar: return Color(red: 1.0, green: 0.27, blue: 0.23)
        case .other: return Color(red: 0.55, green: 0.55, blue: 0.60)
        }
    }

    var bundleID: String? {
        switch self {
        case .sms: return "com.apple.MobileSMS"
        case .whatsapp: return "net.whatsapp.WhatsApp"
        case .email: return "com.apple.mail"
        case .call: return "com.apple.FaceTime"
        case .calendar: return "com.apple.iCal"
        case .other: return nil
        }
    }
}

struct IslandNotice: Identifiable {
    let id = UUID()
    var kind: NoticeKind
    var appName: String
    var title: String
    var body: String
    var date = Date()
    var signature: String = ""
    var onAccept: (() -> Void)?
    var onDecline: (() -> Void)?
}

struct CalEvent: Identifiable, Equatable {
    let id: String
    let title: String
    let start: Date
    let end: Date
    let isAllDay: Bool
    let location: String?
    let color: Color
}

// MARK: - Model

final class IslandModel: ObservableObject {
    enum Mode: Equatable { case collapsed, peek, expanded }

    static let spring = Animation.spring(response: 0.48, dampingFraction: 0.66, blendDuration: 0)

    @Published var mode: Mode = .collapsed
    @Published var current: IslandNotice?
    @Published var history: [IslandNotice] = []
    @Published var events: [CalEvent] = []
    @Published var notch = CGSize(width: 190, height: 32)
    @Published var calendarAuthorized = false
    @Published var accessibilityTrusted = false
    @Published var liquidGlass: Bool = true {
        didSet { UserDefaults.standard.set(liquidGlass, forKey: "liquidGlass") }
    }
    /// 0 = solid black, 1 = most translucent.
    @Published var translucency: Double = 0.6 {
        didSet { UserDefaults.standard.set(translucency, forKey: "translucency") }
    }

    var expandOnHover: Bool {
        didSet { UserDefaults.standard.set(expandOnHover, forKey: "expandOnHover") }
    }

    /// Called *before* the island grows (so the panel is big enough) and ~0.75s *after* it shrinks.
    var layoutHandler: ((CGSize) -> Void)?
    var requestAccessibility: (() -> Void)?
    var requestCalendar: (() -> Void)?

    private var dismissWork: DispatchWorkItem?
    private var hoverWork: DispatchWorkItem?
    private var shrinkWork: DispatchWorkItem?
    private var isHovering = false

    init() {
        expandOnHover = (UserDefaults.standard.object(forKey: "expandOnHover") as? Bool) ?? true
        liquidGlass = (UserDefaults.standard.object(forKey: "liquidGlass") as? Bool) ?? true
        translucency = (UserDefaults.standard.object(forKey: "translucency") as? Double) ?? 0.6
    }

    // MARK: Geometry

    func layoutSize(mode: Mode, notice: IslandNotice?) -> CGSize {
        let n = notch
        switch mode {
        case .collapsed:
            return CGSize(width: n.width, height: n.height)
        case .peek:
            let isCall = notice?.kind == .call
            return CGSize(width: max(n.width + 200, isCall ? 430 : 400),
                          height: n.height + (isCall ? 78 : 62))
        case .expanded:
            return CGSize(width: max(n.width + 380, 600), height: n.height + 190)
        }
    }

    var size: CGSize { layoutSize(mode: mode, notice: current) }
    var topRadius: CGFloat { mode == .collapsed ? 6 : 19 }
    /// The collapsed island is always solid black so it matches the hardware notch.
    var glassAmount: Double { mode == .collapsed ? 0 : translucency }
    var bottomRadius: CGFloat {
        switch mode {
        case .collapsed: return 10
        case .peek: return 22
        case .expanded: return 30
        }
    }

    // MARK: State changes

    private func animate(mode newMode: Mode, notice newNotice: IslandNotice?) {
        let target = layoutSize(mode: newMode, notice: newNotice)
        let cur = size
        shrinkWork?.cancel()

        if target.width > cur.width || target.height > cur.height {
            layoutHandler?(CGSize(width: max(target.width, cur.width),
                                  height: max(target.height, cur.height)))
        }

        withAnimation(IslandModel.spring) {
            mode = newMode
            current = newNotice
        }

        if target.width < cur.width || target.height < cur.height {
            let work = DispatchWorkItem { [weak self] in
                guard let self = self else { return }
                self.layoutHandler?(self.size)
            }
            shrinkWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.75, execute: work)
        }
    }

    func present(_ notice: IslandNotice) {
        history.insert(notice, at: 0)
        if history.count > 20 { history.removeLast(history.count - 20) }

        // A ringing call is never replaced by a regular notification.
        if mode == .peek, current?.kind == .call, notice.kind != .call { return }
        // While the island is open, new items just appear in the list.
        if mode == .expanded, notice.kind != .call { return }

        animate(mode: .peek, notice: notice)
        scheduleDismiss(after: notice.kind == .call ? 60 : 5)
    }

    func collapse() {
        dismissWork?.cancel()
        animate(mode: .collapsed, notice: nil)
    }

    func toggleExpanded() {
        dismissWork?.cancel()
        if mode == .expanded { collapse() } else { animate(mode: .expanded, notice: nil) }
    }

    private func scheduleDismiss(after seconds: TimeInterval) {
        dismissWork?.cancel()
        let work = DispatchWorkItem { [weak self] in
            guard let self = self else { return }
            guard self.mode == .peek else { return }
            if self.isHovering { self.scheduleDismiss(after: 2); return }
            self.collapse()
        }
        dismissWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + seconds, execute: work)
    }

    func hoverChanged(_ hovering: Bool) {
        isHovering = hovering
        hoverWork?.cancel()
        if hovering {
            guard expandOnHover, mode != .expanded else { return }
            let work = DispatchWorkItem { [weak self] in
                guard let self = self, self.isHovering, self.mode != .expanded else { return }
                self.dismissWork?.cancel()
                self.animate(mode: .expanded, notice: nil)
            }
            hoverWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.18, execute: work)
        } else if mode == .expanded {
            let work = DispatchWorkItem { [weak self] in
                guard let self = self, !self.isHovering, self.mode == .expanded else { return }
                self.collapse()
            }
            hoverWork = work
            DispatchQueue.main.asyncAfter(deadline: .now() + 0.35, execute: work)
        }
    }

    /// The system banner for this notification went away (e.g. the call was answered on the phone).
    func noticeGone(signature: String) {
        guard let c = current, c.kind == .call, c.signature == signature, mode == .peek else { return }
        DispatchQueue.main.asyncAfter(deadline: .now() + 0.7) { [weak self] in
            guard let self = self, self.current?.id == c.id, self.mode == .peek else { return }
            self.collapse()
        }
    }

    func open(_ notice: IslandNotice) {
        if let id = notice.kind.bundleID,
           let url = NSWorkspace.shared.urlForApplication(withBundleIdentifier: id) {
            NSWorkspace.shared.openApplication(at: url,
                                               configuration: NSWorkspace.OpenConfiguration(),
                                               completionHandler: nil)
        }
        collapse()
    }

    /// Opens the island for a few seconds so appearance changes can be seen.
    func openForPreview() {
        dismissWork?.cancel()
        if mode != .expanded { animate(mode: .expanded, notice: nil) }
        let work = DispatchWorkItem { [weak self] in
            guard let self = self, !self.isHovering, self.mode == .expanded else { return }
            self.collapse()
        }
        dismissWork = work
        DispatchQueue.main.asyncAfter(deadline: .now() + 4, execute: work)
    }

    func clearHistory() {
        withAnimation(IslandModel.spring) { history.removeAll() }
    }
}
