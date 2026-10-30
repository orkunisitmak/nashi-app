import SwiftUI
import NashiCore

/// Settings › Wheels: hide the convert targets, tools and file actions someone never uses.
struct WheelSettingsView:View {
    @State private var mode=WheelMode.convert
    @State private var hidden:[WheelMode:Set<String>]=Dictionary(uniqueKeysWithValues:WheelMode.allCases.map{($0,WheelVisibility.hidden($0))})
    @State private var convertSample=WheelCustomization.convertSamples[0]
    @State private var toolSample=WheelCustomization.toolSamples[0]
    @AppStorage("conversionFanTheme") private var fanTheme="glass"
    @AppStorage("appLanguage") private var language="en"
    private struct Entry:Identifiable { let id:String;let title:String }
    private var groups:[(String,[Entry])] {
        switch mode {
        case .convert: return WheelCustomization.formatGroups.map{($0.0,$0.1.map{Entry(id:$0,title:$0 == "gz" ? "GZIP":$0.uppercased())})}
        case .tools: return WheelCustomization.toolGroups.map{($0.0,$0.1.map{Entry(id:$0.rawValue,title:FeatureCatalog.toolNames[$0] ?? $0.title)})}
        case .actions: return [("",FileAction.allCases.map{Entry(id:$0.rawValue,title:$0.title)})]
        }
    }
    private var sample:WheelSample? { mode == .convert ? convertSample:mode == .tools ? toolSample:nil }
    private var hiddenHere:Set<String> { hidden[mode] ?? [] }
    private var total:Int { groups.reduce(0){$0+$1.1.count} }
    private var visibleCount:Int { groups.flatMap(\.1).filter{!hiddenHere.contains($0.id)}.count }
    private var lastAction:Bool { mode == .actions && visibleCount == 1 }
    private func chord(_ mode:WheelMode)->ModifierChord { ModifierPreferences.chord(for:mode == .convert ? .conversions:mode == .tools ? .tools:.actions) }
    var body:some View {
        ScrollView { VStack(alignment:.leading,spacing:18) {
            NashiPageHeading(eyebrow:"Wheels",title:"Only what you need.",subtitle:"Hide what you never use. The wheels share the space among the rest.").padding(.bottom,4)
            Picker("Wheels",selection:$mode) { ForEach(WheelMode.allCases,id:\.self) { mode in Text(verbatim:"\(AppLocalization.text(mode.rawValue.capitalized,language:language))  \(chord(mode).symbols)").tag(mode) } }.labelsHidden().pickerStyle(.segmented).fixedSize()
            LText("Opens with \(chord(mode).symbols) \(chord(mode).title) while dragging files.").font(.caption).foregroundStyle(.secondary).padding(.top,-8)
            HStack(alignment:.top,spacing:20) {
                VStack(alignment:.leading,spacing:14) {
                    ForEach(groups,id:\.0) { group in VStack(alignment:.leading,spacing:6) {
                        if !group.0.isEmpty { LText(group.0.uppercased()).font(.system(size:9,weight:.bold,design:.rounded)).tracking(1).foregroundStyle(.secondary) }
                        ChipFlow(spacing:6) { ForEach(group.1) { entry in chip(entry) } }
                    } }
                    LText("Click an item to hide or show it.").font(.caption).foregroundStyle(.secondary)
                    if lastAction { LText("At least one action stays visible.").font(.caption).foregroundStyle(NashiPalette.goldInk) }
                }.frame(maxWidth:.infinity,alignment:.leading)
                VStack(spacing:10) {
                    if mode == .convert { samplePicker(WheelCustomization.convertSamples,$convertSample) }
                    if mode == .tools { samplePicker(WheelCustomization.toolSamples,$toolSample) }
                    WheelPreview(mode:mode,items:WheelItem.fileItems(mode:mode,inputs:sample?.inputs ?? [],hidden:hiddenHere),backdrop:true)
                        .id(fanTheme).frame(width:260,height:260).clipShape(Circle()).allowsHitTesting(false).accessibilityHidden(true)
                    LText("\(visibleCount) of \(total) visible").font(.caption).monospacedDigit().foregroundStyle(.secondary)
                    Button { save([]) } label:{ LText("Show all") }.disabled(hiddenHere.isEmpty)
                }.frame(width:260)
            }
        }.padding(26).frame(maxWidth:680) }.frame(maxWidth:.infinity)
    }
    private func chip(_ entry:Entry)->some View {
        let shown = !hiddenHere.contains(entry.id)
        return Button { toggle(entry.id) } label:{
            LText(entry.title).strikethrough(!shown).font(.system(size:11,weight:shown ? .semibold:.medium,design:.rounded))
                .foregroundStyle(shown ? NashiPalette.forest:Color.secondary).padding(.horizontal,10).padding(.vertical,5)
                .background(shown ? NashiPalette.sage:Color.clear,in:Capsule())
                .overlay(Capsule().strokeBorder(shown ? Color.clear:Color.secondary.opacity(0.4),style:StrokeStyle(lineWidth:1,dash:[3])))
        }.buttonStyle(.plain).disabled(shown && lastAction).accessibilityAddTraits(shown ? .isSelected:[])
    }
    private func samplePicker(_ samples:[WheelSample],_ selection:Binding<WheelSample>)->some View {
        HStack { LText("Preview with");Picker("Preview with",selection:selection) { ForEach(samples) { Text(verbatim:$0.title).tag($0) } }.labelsHidden().frame(width:120) }.font(.caption)
    }
    private func toggle(_ id:String) {
        var ids=hiddenHere
        if ids.remove(id) == nil { ids.insert(id) }
        save(ids)
        // Show the change: switch the preview to a sample whose wheel contains this item.
        if mode == .convert,!FeatureCatalog.formats(for:convertSample.inputs).contains(id),let next=WheelCustomization.firstConvertSample(showing:id) { convertSample=next }
        if mode == .tools,let tool=ToolKind(rawValue:id),!FeatureCatalog.tools(for:toolSample.inputs).contains(tool),let next=WheelCustomization.firstToolSample(showing:tool) { toolSample=next }
    }
    private func save(_ ids:Set<String>) { hidden[mode]=ids;WheelVisibility.setHidden(ids,for:mode) }
}

/// Wraps chips onto as many lines as the width needs.
private struct ChipFlow:Layout {
    var spacing:CGFloat
    func sizeThatFits(proposal:ProposedViewSize,subviews:Subviews,cache:inout ())->CGSize { arrange(proposal.width ?? .infinity,subviews).size }
    func placeSubviews(in bounds:CGRect,proposal:ProposedViewSize,subviews:Subviews,cache:inout ()) {
        for (index,point) in arrange(bounds.width,subviews).points.enumerated() { subviews[index].place(at:CGPoint(x:bounds.minX+point.x,y:bounds.minY+point.y),proposal:.unspecified) }
    }
    private func arrange(_ width:CGFloat,_ subviews:Subviews)->(points:[CGPoint],size:CGSize) {
        var points:[CGPoint]=[],x:CGFloat=0,y:CGFloat=0,line:CGFloat=0,widest:CGFloat=0
        for subview in subviews {
            let size=subview.sizeThatFits(.unspecified)
            if x>0,x+size.width>width { x=0;y+=line+spacing;line=0 }
            points.append(CGPoint(x:x,y:y));x+=size.width+spacing;line=max(line,size.height);widest=max(widest,x-spacing)
        }
        return (points,CGSize(width:widest,height:y+line))
    }
}
