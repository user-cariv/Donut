import SwiftUI

struct IslandView: View {
    @ObservedObject var model: IslandModel

    var body: some View {
        let size = model.size
        let shape = NotchShape(topRadius: model.topRadius, bottomRadius: model.bottomRadius)
        let t = model.glassAmount

        ZStack(alignment: .top) {
            // Shadow caster; fades away as the island turns translucent.
            shape
                .fill(Color.black)
                .opacity(1 - t)
                .frame(width: size.width, height: size.height)
                .shadow(color: Color.black.opacity(model.mode == .collapsed ? 0 : 0.45),
                        radius: 16, x: 0, y: 8)

            // Solid black or blurred glass, depending on the user's settings.
            IslandMaterial(model: model, shape: shape, amount: t)
                .frame(width: size.width, height: size.height)
                .clipShape(shape)

            // Glass rim light.
            if model.liquidGlass {
                shape
                    .stroke(LinearGradient(colors: [Color.white.opacity(0.65),
                                                    Color.white.opacity(0.06),
                                                    Color.white.opacity(0.30)],
                                           startPoint: .topLeading,
                                           endPoint: .bottomTrailing),
                            lineWidth: 1.2)
                    .frame(width: size.width, height: size.height)
                    .opacity(t)
            }

            // Subtle edge, and a colored glow while a notification is showing.
            shape
                .stroke(glowColor.opacity(model.mode == .peek ? 0.55 : 0.07), lineWidth: 1)
                .frame(width: size.width, height: size.height)

            ZStack(alignment: .top) {
                switch model.mode {
                case .collapsed:
                    Color.clear
                case .peek:
                    if let notice = model.current {
                        PeekView(model: model, notice: notice)
                            .transition(.liquid)
                    }
                case .expanded:
                    ExpandedView(model: model)
                        .transition(.liquid)
                }
            }
            .frame(width: size.width, height: size.height, alignment: .top)
            .clipShape(shape)
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
        .animation(.easeInOut(duration: 0.25), value: model.liquidGlass)
        .animation(.easeOut(duration: 0.15), value: model.translucency)
        .ignoresSafeArea()
    }

    private var glowColor: Color {
        model.current?.kind.color ?? Color.white
    }
}

// MARK: - Peek (notification / call)

struct PeekView: View {
    @ObservedObject var model: IslandModel
    let notice: IslandNotice

    var body: some View {
        VStack(spacing: 0) {
            // Compact "leading / trailing" band, either side of the notch.
            HStack(spacing: 0) {
                IconBadge(kind: notice.kind, size: 22)
                    .padding(.leading, 22)
                Spacer(minLength: model.notch.width)
                trailing
                    .padding(.trailing, 24)
            }
            .frame(height: model.notch.height)

            HStack(spacing: 12) {
                VStack(alignment: .leading, spacing: 2) {
                    Text(notice.title)
                        .font(.system(size: 14, weight: .semibold))
                        .foregroundColor(.white)
                        .lineLimit(1)
                    if !notice.body.isEmpty {
                        Text(notice.body)
                            .font(.system(size: 12.5))
                            .foregroundColor(Color.white.opacity(0.75))
                            .lineLimit(notice.kind == .call ? 1 : 2)
                    }
                }
                Spacer(minLength: 8)
                if notice.kind == .call {
                    HStack(spacing: 10) {
                        CircleActionButton(symbol: "phone.down.fill", color: Color(red: 1.0, green: 0.23, blue: 0.19)) {
                            notice.onDecline?()
                            model.collapse()
                        }
                        CircleActionButton(symbol: "phone.fill", color: Color(red: 0.20, green: 0.78, blue: 0.35)) {
                            notice.onAccept?()
                            model.collapse()
                        }
                    }
                }
            }
            .padding(.horizontal, 30)
            .padding(.top, 2)

            Spacer(minLength: 0)
        }
        .contentShape(Rectangle())
        .onTapGesture {
            if notice.kind != .call { model.open(notice) }
        }
    }

    @ViewBuilder
    private var trailing: some View {
        if notice.kind == .call {
            WaveformView(color: notice.kind.color)
        } else {
            Text(notice.appName)
                .font(.system(size: 11, weight: .medium))
                .foregroundColor(Color.white.opacity(0.65))
                .lineLimit(1)
        }
    }
}

// MARK: - Expanded

struct ExpandedView: View {
    @ObservedObject var model: IslandModel
    @State private var showSettings = false

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 0) {
                Text(Date.now, format: .dateTime.weekday(.wide).day().month(.abbreviated))
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(Color.white.opacity(0.8))
                    .padding(.leading, 30)
                Spacer(minLength: model.notch.width)
                HStack(spacing: 14) {
                    if !showSettings {
                        Button(action: { model.clearHistory() }) {
                            Text("Clear")
                                .font(.system(size: 11, weight: .medium))
                                .foregroundColor(Color.white.opacity(model.history.isEmpty ? 0.3 : 0.75))
                        }
                        .buttonStyle(SquishStyle())
                        .disabled(model.history.isEmpty)
                    }
                    Button(action: {
                        withAnimation(IslandModel.spring) { showSettings.toggle() }
                    }) {
                        Image(systemName: showSettings ? "xmark" : "slider.horizontal.3")
                            .font(.system(size: 12, weight: .semibold))
                            .foregroundColor(Color.white.opacity(0.85))
                            .frame(width: 22, height: 22)
                    }
                    .buttonStyle(SquishStyle())
                }
                .padding(.trailing, 30)
            }
            .frame(height: model.notch.height)

            ZStack(alignment: .top) {
                if showSettings {
                    settingsPanel
                        .transition(.opacity)
                } else {
                    columns
                        .transition(.opacity)
                }
            }
            .padding(.horizontal, 34)
            .padding(.top, 8)
            .padding(.bottom, 18)
        }
        .environment(\.colorScheme, .dark)
    }

    // MARK: Columns

    private var columns: some View {
        HStack(alignment: .top, spacing: 18) {
            calendarColumn
                .frame(maxWidth: .infinity, alignment: .topLeading)
            Rectangle()
                .fill(Color.white.opacity(0.14))
                .frame(width: 1)
                .padding(.vertical, 2)
            notificationsColumn
                .frame(maxWidth: .infinity, alignment: .topLeading)
        }
    }

    // MARK: Settings

    private var settingsPanel: some View {
        VStack(alignment: .leading, spacing: 14) {
            HStack {
                VStack(alignment: .leading, spacing: 2) {
                    Text("Liquid Glass")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundColor(.white)
                    Text("Apple Liquid Glass, styled to echo the iPhone look on macOS 27")
                        .font(.system(size: 11))
                        .foregroundColor(Color.white.opacity(0.6))
                }
                Spacer()
                Toggle("", isOn: $model.liquidGlass)
                    .labelsHidden()
                    .toggleStyle(.switch)
            }

            VStack(alignment: .leading, spacing: 6) {
                HStack {
                    Text("Translucency")
                        .font(.system(size: 13, weight: .semibold))
                        .foregroundColor(.white)
                    Spacer()
                    Text(model.translucency < 0.02
                         ? "Solid"
                         : "\(Int((model.translucency * 100).rounded()))%")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(Color.white.opacity(0.7))
                }
                Slider(value: $model.translucency, in: 0...1)
                    .tint(Color.white.opacity(0.85))
            }
        }
        .frame(maxWidth: .infinity, alignment: .topLeading)
    }

    // MARK: Left: calendar

    private var calendarColumn: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("UP NEXT")
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(Color.white.opacity(0.55))
            if !model.calendarAuthorized {
                linkButton("Connect Calendar") { model.requestCalendar?() }
            } else if model.events.isEmpty {
                Text("Nothing scheduled")
                    .font(.system(size: 12.5))
                    .foregroundColor(Color.white.opacity(0.6))
            } else {
                ForEach(Array(model.events.prefix(3))) { event in
                    HStack(spacing: 10) {
                        RoundedRectangle(cornerRadius: 2)
                            .fill(event.color)
                            .frame(width: 4, height: 32)
                        VStack(alignment: .leading, spacing: 2) {
                            Text(event.title)
                                .font(.system(size: 13, weight: .semibold))
                                .foregroundColor(.white)
                                .lineLimit(1)
                            Text(timeText(event))
                                .font(.system(size: 11.5))
                                .foregroundColor(Color.white.opacity(0.7))
                                .lineLimit(1)
                        }
                    }
                }
            }
            Spacer(minLength: 0)
        }
    }

    private func timeText(_ e: CalEvent) -> String {
        if e.isAllDay { return "All day" }
        let cal = Calendar.current
        let start = e.start.formatted(date: .omitted, time: .shortened)
        let end = e.end.formatted(date: .omitted, time: .shortened)
        let day = cal.isDateInToday(e.start) ? "" : (cal.isDateInTomorrow(e.start) ? "Tomorrow, " : "")
        return "\(day)\(start) – \(end)"
    }

    // MARK: Right: recent notifications

    private var notificationsColumn: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("RECENT")
                .font(.system(size: 10, weight: .bold))
                .foregroundColor(Color.white.opacity(0.55))
            if !model.accessibilityTrusted {
                linkButton("Enable messages & calls") { model.requestAccessibility?() }
            } else if model.history.isEmpty {
                Text("No new notifications")
                    .font(.system(size: 12.5))
                    .foregroundColor(Color.white.opacity(0.6))
            } else {
                ForEach(Array(model.history.prefix(3))) { n in
                    Button(action: { model.open(n) }) {
                        HStack(spacing: 10) {
                            IconBadge(kind: n.kind, size: 28)
                            VStack(alignment: .leading, spacing: 2) {
                                Text(n.title)
                                    .font(.system(size: 13, weight: .semibold))
                                    .foregroundColor(.white)
                                    .lineLimit(1)
                                Text(n.body.isEmpty ? n.appName : n.body)
                                    .font(.system(size: 11.5))
                                    .foregroundColor(Color.white.opacity(0.7))
                                    .lineLimit(1)
                            }
                            Spacer(minLength: 0)
                        }
                        .contentShape(Rectangle())
                    }
                    .buttonStyle(SquishStyle())
                }
            }
            Spacer(minLength: 0)
        }
    }

    private func linkButton(_ title: String, action: @escaping () -> Void) -> some View {
        Button(action: action) {
            Text(title)
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(.white)
                .padding(.horizontal, 12)
                .padding(.vertical, 6)
                .background(Capsule().fill(Color.white.opacity(0.18)))
        }
        .buttonStyle(SquishStyle())
    }
}
