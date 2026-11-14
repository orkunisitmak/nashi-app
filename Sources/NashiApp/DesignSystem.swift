import AppKit
import SwiftUI

// Shared colors. appTheme selects Nashi or Morphicfolio; appearance still selects light, dark, or system.
enum NashiTheme:String,CaseIterable {
    case nashi,morphicfolio
    var title:String { self == .nashi ? "Nashi":"Morphicfolio" }
    static var current:NashiTheme { NashiTheme(rawValue:UserDefaults.standard.string(forKey:"appTheme") ?? "nashi") ?? .nashi }
}
enum NashiPalette {
    static func adaptive(_ nashiLight:(Double,Double,Double),_ nashiDark:(Double,Double,Double),_ morphicLight:(Double,Double,Double),_ morphicDark:(Double,Double,Double))->NSColor {
        NSColor(name:nil) { appearance in
            let dark=appearance.bestMatch(from:[.darkAqua,.aqua]) == .darkAqua
            let rgb=NashiTheme.current == .morphicfolio ? (dark ? morphicDark:morphicLight):(dark ? nashiDark:nashiLight)
            return NSColor(srgbRed:rgb.0,green:rgb.1,blue:rgb.2,alpha:1)
        }
    }
    static let canvas=Color(nsColor:adaptive((0.976,0.965,0.930),(0.065,0.100,0.079),(0.965,0.961,0.949),(0.075,0.071,0.067)))
    static let surface=Color(nsColor:adaptive((1,0.993,0.967),(0.110,0.155,0.122),(0.992,0.988,0.976),(0.118,0.110,0.102)))
    static let inset=Color(nsColor:adaptive((0.945,0.930,0.880),(0.082,0.121,0.094),(0.910,0.902,0.878),(0.157,0.149,0.137)))
    static let slateInk=adaptive((0.145,0.165,0.118),(0.925,0.949,0.906),(0.102,0.094,0.086),(0.945,0.929,0.902))
    static let pageInk=Color(nsColor:slateInk)
    static let forest=Color(nsColor:adaptive((0.19,0.33,0.23),(0.70,0.82,0.62),(0.176,0.165,0.149),(0.910,0.886,0.835)))
    static let sand=Color(nsColor:adaptive((0.965,0.906,0.746),(0.235,0.205,0.130),(0.933,0.910,0.855),(0.227,0.196,0.149)))
    static let sage=Color(nsColor:adaptive((0.877,0.921,0.824),(0.140,0.221,0.156),(0.910,0.894,0.855),(0.227,0.208,0.176)))
    static let border=Color.primary.opacity(0.075)
    static let gold=adaptive((0.89,0.72,0.35),(0.89,0.72,0.35),(0.145,0.133,0.118),(0.910,0.855,0.745))
    static let ink=adaptive((0.20,0.25,0.14),(0.20,0.25,0.14),(0.965,0.953,0.925),(0.102,0.090,0.075))
    static let goldInk=Color(nsColor:adaptive((0.44,0.31,0.09),(0.91,0.77,0.46),(0.290,0.255,0.196),(0.910,0.835,0.690)))
    static let buttonInk=Color(nsColor:adaptive((0.17,0.21,0.11),(0.17,0.21,0.11),(0.965,0.953,0.925),(0.102,0.090,0.075)))
    static let goldEnd=Color(nsColor:adaptive((0.96,0.82,0.49),(0.96,0.82,0.49),(0.290,0.267,0.235),(0.965,0.925,0.835)))
}
extension Color { static let nashi=Color(nsColor:NashiPalette.gold) }

enum NashiMotion {
    static var enabled:Bool { !UserDefaults.standard.bool(forKey:"reduceAnimations") && !NSWorkspace.shared.accessibilityDisplayShouldReduceMotion }
}
enum NashiAppearance {
    @MainActor static func apply(_ choice:String?=nil) {
        let choice=choice ?? UserDefaults.standard.string(forKey:"appAppearance") ?? "system"
        NSApp.appearance=choice=="light" ? NSAppearance(named:.aqua):choice=="dark" ? NSAppearance(named:.darkAqua):nil
    }
}

struct NashiMotionModifier<Value:Equatable>:ViewModifier {
    let value:Value
    @Environment(\.accessibilityReduceMotion) private var systemReduced
    @AppStorage("reduceAnimations") private var reduced=false
    func body(content:Content)->some View { content.animation(systemReduced || reduced ? nil:.spring(response:0.32,dampingFraction:0.88),value:value) }
}
struct NashiEntrance:ViewModifier {
    var delay=0.0
    @State private var visible=false
    @Environment(\.accessibilityReduceMotion) private var systemReduced
    @AppStorage("reduceAnimations") private var reduced=false
    func body(content:Content)->some View {
        content.opacity(visible ? 1:0).offset(y:visible || systemReduced || reduced ? 0:8)
            .onAppear {
                if systemReduced || reduced { visible=true }
                else { withAnimation(.easeOut(duration:0.24).delay(delay)) { visible=true } }
            }
    }
}
extension View {
    func nashiAnimation<V:Equatable>(value:V)->some View { modifier(NashiMotionModifier(value:value)) }
    func nashiEntrance(delay:Double=0)->some View { modifier(NashiEntrance(delay:delay)) }
    func nashiCard(radius:CGFloat=22)->some View {
        background(NashiPalette.surface,in:RoundedRectangle(cornerRadius:radius,style:.continuous))
            .overlay(RoundedRectangle(cornerRadius:radius,style:.continuous).strokeBorder(NashiPalette.border,lineWidth:1))
    }
}

enum NashiButtonKind { case primary,secondary,quiet }
struct NashiButtonStyle:ButtonStyle {
    var kind:NashiButtonKind = .secondary
    var compact=false
    init(_ kind:NashiButtonKind = .secondary,compact:Bool=false) { self.kind=kind;self.compact=compact }
    func makeBody(configuration:Configuration)->some View { NashiButtonBody(configuration:configuration,kind:kind,compact:compact) }
}
private struct NashiButtonBody:View {
    let configuration:ButtonStyleConfiguration
    let kind:NashiButtonKind
    let compact:Bool
    @State private var hovered=false
    @Environment(\.isEnabled) private var enabled
    @Environment(\.isFocused) private var focused
    @Environment(\.accessibilityReduceMotion) private var systemReduced
    @AppStorage("reduceAnimations") private var reduced=false
    private var primary:Bool { kind == .primary }
    var body:some View {
        configuration.label.font(.system(size:compact ? 11:13,weight:.semibold,design:.rounded))
            .multilineTextAlignment(.center)
            .fixedSize(horizontal:false,vertical:true)
            .frame(minHeight:compact ? 14:18,alignment:.center)
            .padding(.horizontal,compact ? 10:15).padding(.vertical,compact ? 7:11)
            .foregroundStyle(primary ? NashiPalette.buttonInk:Color.primary)
            .background {
                RoundedRectangle(cornerRadius:compact ? 10:13,style:.continuous)
                    .fill(primary ? AnyShapeStyle(LinearGradient(colors:[Color.nashi,NashiPalette.goldEnd],startPoint:.leading,endPoint:.trailing)):AnyShapeStyle(kind == .quiet ? Color.clear:NashiPalette.surface))
                    .overlay(RoundedRectangle(cornerRadius:compact ? 10:13,style:.continuous).strokeBorder(primary ? Color.white.opacity(0.12):NashiPalette.border,lineWidth:kind == .quiet ? 0:1))
            }
            .overlay(RoundedRectangle(cornerRadius:compact ? 10:13).fill(Color.primary.opacity(hovered && !primary ? 0.035:0)))
            .overlay(RoundedRectangle(cornerRadius:compact ? 10:13).strokeBorder(focused ? NashiPalette.goldInk:Color.clear,lineWidth:2).padding(-3))
            .shadow(color:primary && enabled ? Color.nashi.opacity(hovered ? 0.20:0.10):.clear,radius:hovered ? 9:5,y:3)
            .scaleEffect(systemReduced || reduced ? 1:configuration.isPressed ? 0.97:hovered && enabled ? 1.015:1)
            .opacity(enabled ? 1:0.4).contentShape(RoundedRectangle(cornerRadius:13))
            .onHover { hovered=$0 }
            .animation(systemReduced || reduced ? nil:.easeOut(duration:0.14),value:configuration.isPressed)
            .animation(systemReduced || reduced ? nil:.easeOut(duration:0.16),value:hovered)
    }
}
struct NashiGroupBoxStyle:GroupBoxStyle {
    func makeBody(configuration:Configuration)->some View {
        VStack(alignment:.leading,spacing:16) {
            configuration.label.font(.system(size:13,weight:.semibold,design:.rounded)).foregroundStyle(NashiPalette.forest)
            configuration.content
        }.padding(20).frame(maxWidth:.infinity,alignment:.leading).nashiCard()
    }
}

struct NashiMark:View {
    var size:CGFloat=44
    var happy=false
    var body:some View {
        Image(nsImage:NashiArtwork.mark(size:size*2,happy:happy)).resizable().interpolation(.high).frame(width:size,height:size).accessibilityHidden(true)
    }
}

struct NashiPill:View {
    let title:String
    var symbol:String?=nil
    var color:Color=NashiPalette.forest
    var body:some View {
        HStack(spacing:5) { if let symbol { Image(systemName:symbol) };LText(title) }
            .font(.system(size:10,weight:.semibold,design:.rounded)).foregroundStyle(color == .nashi ? NashiPalette.goldInk:color)
            .padding(.horizontal,9).padding(.vertical,5).background(color.opacity(0.09),in:Capsule())
    }
}
struct NashiPageHeading:View {
    let eyebrow:String
    let title:String
    let subtitle:String
    var body:some View {
        VStack(alignment:.leading,spacing:8) {
            LText(eyebrow.uppercased()).font(.system(size:10,weight:.bold,design:.rounded)).tracking(1.4).foregroundStyle(NashiPalette.goldInk)
            LText(title).font(.system(size:27,weight:.bold,design:.rounded)).tracking(-0.7)
            LText(subtitle).font(.system(size:12)).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
        }
    }
}
struct NashiCheck:View {
    @State private var arrived=false
    var body:some View {
        Image(systemName:"checkmark").font(.system(size:14,weight:.bold)).foregroundStyle(NashiPalette.forest)
            .frame(width:34,height:34).background(NashiPalette.sage,in:Circle())
            .scaleEffect(arrived ? 1:0.75).opacity(arrived ? 1:0)
            .nashiAnimation(value:arrived).onAppear{arrived=true}.accessibilityHidden(true)
    }
}
