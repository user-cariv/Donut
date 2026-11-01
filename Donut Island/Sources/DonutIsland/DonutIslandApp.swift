import SwiftUI

@main
struct DonutIslandApp: App {
    @NSApplicationDelegateAdaptor(AppDelegate.self) var appDelegate

    var body: some Scene {
        // The island lives in its own panel (see AppDelegate); no regular windows.
        Settings { EmptyView() }
    }
}
