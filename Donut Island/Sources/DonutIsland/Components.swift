import AppKit
import SwiftUI

// MARK: - Notch shape (animatable corner radii)

/// Black "hanging" shape: the top corners flare outward into the screen edge,
/// the bottom corners are rounded. Both radii animate, which is what makes the
/// morph between collapsed / peek / expanded feel fluid.
struct NotchShape: Shape {
    var topRadius: CGFloat
    var bottomRadius: CGFloat

    var animatableData: AnimatablePair<CGFloat, CGFloat> {
        get { AnimatablePair(topRadius, bottomRadius) }
        set {
            topRadius = newValue.first
            bottomRadius = newValue.second
        }
    }

    func path(in rect: CGRect) -> Path {
        let tr = min(topRadius, rect.width / 4)
        let br = min(bottomRadius, rect.height / 2, rect.width / 4)
        var p = Path()
        p.move(to: CGPoint(x: rect.minX, y: rect.minY))
        p.addQuadCurve(to: CGPoint(x: rect.minX + tr, y: rect.minY + tr),
                       control: CGPoint(x: rect.minX + tr, y: rect.minY))
        p.addLine(to: CGPoint(x: rect.minX + tr, y: rect.maxY - br))
        p.addQuadCurve(to: CGPoint(x: rect.minX + tr + br, y: rect.maxY),
                       control: CGPoint(x: rect.minX + tr, y: rect.maxY))
        p.addLine(to: CGPoint(x: rect.maxX - tr - br, y: rect.maxY))
        p.addQuadCurve(to: CGPoint(x: rect.maxX - tr, y: rect.maxY - br),
                       control: CGPoint(x: rect.maxX - tr, y: rect.maxY))
        p.addLine(to: CGPoint(x: rect.maxX - tr, y: rect.minY + tr))
        p.addQuadCurve(to: CGPoint(x: rect.maxX, y: rect.minY),
                       control: CGPoint(x: rect.maxX - tr, y: rect.minY))
        p.closeSubpath()
        return p
    }
}

// MARK: - Liquid transition (blur + squash + fade)

struct BlurModifier: ViewModifier {
    let radius: CGFloat
    func body(content: Content) -> some View {
        content.blur(radius: radius)
    }
}

extension AnyTransition {
    static var liquid: AnyTransition {
        AnyTransition
            .modifier(active: BlurModifier(radius: 14), identity: BlurModifier(radius: 0))
            .combined(with: .scale(scale: 0.82, anchor: .top))
            .combined(with: .opacity)
    }
}

// MARK: - Buttons

struct SquishStyle: ButtonStyle {
    func makeBody(configuration: Configuration) -> some View {
        configuration.label
            .scaleEffect(configuration.isPressed ? 0.86 : 1)
            .animation(.spring(response: 0.25, dampingFraction: 0.55), value: configuration.isPressed)
    }
}

struct CircleActionButton: View {
    let symbol: String
    let color: Color
    let action: () -> Void

    var body: some View {
        Button(action: action) {
            Image(systemName: symbol)
                .font(.system(size: 15, weight: .bold))
                .foregroundColor(.white)
                .frame(width: 40, height: 40)
                .background(Circle().fill(color))
        }
        .buttonStyle(SquishStyle())
    }
}

// MARK: - Small pieces

struct IconBadge: View {
    let kind: NoticeKind
    var size: CGFloat = 26

    var body: some View {
        RoundedRectangle(cornerRadius: size * 0.30, style: .continuous)
            .fill(kind.color)
            .frame(width: size, height: size)
            .overlay(
                Image(systemName: kind.symbol)
                    .font(.system(size: size * 0.5, weight: .semibold))
                    .foregroundColor(.white)
            )
    }
}

struct WaveformView: View {
    var color: Color

    var body: some View {
        TimelineView(.animation) { context in
            let t = context.date.timeIntervalSinceReferenceDate
            HStack(spacing: 2.5) {
                ForEach(0..<5, id: \.self) { i in
                    let phase = sin(t * 4.2 + Double(i) * 0.9)
                    Capsule()
                        .fill(color)
                        .frame(width: 3, height: CGFloat(6.0 + 13.0 * abs(phase)))
                }
            }
            .frame(height: 22)
        }
    }
}

// MARK: - Glass material

/// Real blur of whatever is behind the island (wallpaper, windows). Works on every macOS version.
struct VisualEffectBlur: NSViewRepresentable {
    func makeNSView(context: Context) -> NSVisualEffectView {
        let v = NSVisualEffectView()
        v.material = .hudWindow
        v.blendingMode = .behindWindow
        v.state = .active
        v.appearance = NSAppearance(named: .vibrantDark)
        return v
    }

    func updateNSView(_ nsView: NSVisualEffectView, context: Context) {}
}

/// Apple's own Liquid Glass on macOS 26+. It is only compiled when building with the
/// Xcode 26 toolchain, and falls back to nothing on older systems.
struct GlassLayer: View {
    let shape: NotchShape

    @ViewBuilder
    var body: some View {
        #if compiler(>=6.2)
        if #available(macOS 26.0, *) {
            Color.clear.glassEffect(.regular, in: shape)
        } else {
            Color.clear
        }
        #else
        Color.clear
        #endif
    }
}

/// Everything behind the island's content:
///   amount 0 = solid black (blends into the hardware notch), 1 = most translucent.
struct IslandMaterial: View {
    @ObservedObject var model: IslandModel
    let shape: NotchShape
    let amount: Double

    var body: some View {
        ZStack(alignment: .top) {
            VisualEffectBlur()
                .opacity(amount)

            Color.black.opacity(1.0 - 0.78 * amount)

            if model.liquidGlass {
                GlassLayer(shape: shape)
                    .opacity(amount)
                // Soft specular sheen across the top, like light catching glass.
                LinearGradient(colors: [Color.white.opacity(0.22), Color.white.opacity(0.0)],
                               startPoint: .top, endPoint: .center)
                    .opacity(amount)
            }

            // Darkens the band around the hardware notch so the glass fades into it.
            VStack(spacing: 0) {
                LinearGradient(colors: [Color.black.opacity(0.95), Color.black.opacity(0.0)],
                               startPoint: .top, endPoint: .bottom)
                    .frame(height: model.notch.height + 14)
                    .opacity(amount)
                Spacer(minLength: 0)
            }
        }
    }
}
