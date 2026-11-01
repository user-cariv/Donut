import EventKit
import SwiftUI
import AppKit

/// Reads every calendar that macOS Calendar knows about, which includes Google
/// (added via System Settings > Internet Accounts), iCloud, Exchange, etc.
final class CalendarManager {
    private let store = EKEventStore()
    private weak var model: IslandModel?
    private var timer: Timer?
    private var announced = Set<String>()

    init(model: IslandModel) {
        self.model = model
    }

    func start() {
        NotificationCenter.default.addObserver(forName: .EKEventStoreChanged,
                                               object: store,
                                               queue: .main) { [weak self] _ in
            self?.refresh()
        }
        requestAccess()
        timer = Timer.scheduledTimer(withTimeInterval: 30, repeats: true) { [weak self] _ in
            self?.tick()
        }
    }

    private func isAuthorized(_ status: EKAuthorizationStatus) -> Bool {
        if #available(macOS 14.0, *) {
            return status == .fullAccess
        } else {
            return status == .authorized
        }
    }

    func requestAccess() {
        let status = EKEventStore.authorizationStatus(for: .event)
        if isAuthorized(status) {
            model?.calendarAuthorized = true
            refresh()
            return
        }

        NSApp.activate(ignoringOtherApps: true)
        let handler: (Bool, Error?) -> Void = { [weak self] granted, _ in
            DispatchQueue.main.async {
                self?.model?.calendarAuthorized = granted
                if granted { self?.refresh() }
            }
        }
        if #available(macOS 14.0, *) {
            store.requestFullAccessToEvents(completion: handler)
        } else {
            store.requestAccess(to: .event, completion: handler)
        }
    }

    func refresh() {
        guard isAuthorized(EKEventStore.authorizationStatus(for: .event)) else { return }
        let cal = Calendar.current
        let now = Date()
        let dayStart = cal.startOfDay(for: now)
        guard let end = cal.date(byAdding: .day, value: 2, to: dayStart) else { return }

        let predicate = store.predicateForEvents(withStart: dayStart, end: end, calendars: nil)
        let items: [CalEvent] = store.events(matching: predicate)
            .filter { $0.endDate > now }
            .sorted { $0.startDate < $1.startDate }
            .map { e in
                CalEvent(id: (e.eventIdentifier ?? UUID().uuidString) + "-\(e.startDate.timeIntervalSince1970)",
                         title: e.title ?? "Event",
                         start: e.startDate,
                         end: e.endDate,
                         isAllDay: e.isAllDay,
                         location: (e.location?.isEmpty == false) ? e.location : nil,
                         color: Color(nsColor: e.calendar.color))
            }
        model?.calendarAuthorized = true
        model?.events = items
    }

    /// Runs every 30s: refreshes the list and pops the island 10 minutes before an event.
    func tick() {
        refresh()
        guard let model = model else { return }
        let now = Date()
        for e in model.events where !e.isAllDay {
            let delta = e.start.timeIntervalSince(now)
            if delta <= 600 && delta > -60 && !announced.contains(e.id) {
                announced.insert(e.id)
                let minutes = Int((delta / 60).rounded(.up))
                var body = delta > 60 ? "Starts in \(minutes) min" : "Starting now"
                if let loc = e.location { body += " · \(loc)" }
                model.present(IslandNotice(kind: .calendar,
                                           appName: "Calendar",
                                           title: e.title,
                                           body: body))
            }
        }
    }
}
