import AppKit
import SwiftUI
import NashiCore

@MainActor final class NotchPanelModel: ObservableObject {
    enum Tab: String, CaseIterable, Identifiable {
        case drop, tools, history, widgets
        var id: String { rawValue }
        var title: String { switch self { case .drop: return "Drop"; case .tools: return "Tools"; case .history: return "History"; case .widgets: return "Widgets" } }
        var symbol: String { switch self { case .drop: return "square.and.arrow.down"; case .tools: return "square.grid.3x3"; case .history: return "clock.arrow.circlepath"; case .widgets: return "sparkles" } }
    }

    @Published private(set) var isExpanded=false
    @Published private(set) var pointerInside=false
    @Published private(set) var dropTargeted=false
    @Published var selectedTab=Tab.drop
    @Published var quickNote:String { didSet { defaults.set(quickNote,forKey:"notchQuickNote") } }
    @Published var tasks:[QuickTask] { didSet { persistTasks() } }
    @Published var taskDraft=""
    @Published private(set) var focusRemainingSeconds:Int
    @Published private(set) var focusDurationSeconds:Int
    @Published private(set) var focusTimerRunning=false
    private var interaction=NotchInteractionState()
    private var collapseWork: DispatchWorkItem?
    private let defaults:UserDefaults
    private var focusTimer=NotchFocusTimer()
    private var focusTick:Timer?
    var onExpandedChange: ((Bool)->Void)?

    struct QuickTask:Identifiable,Codable,Equatable {
        var id=UUID()
        var title:String
        var isComplete=false
    }

    init(defaults:UserDefaults = .standard) {
        self.defaults=defaults
        quickNote=defaults.string(forKey:"notchQuickNote") ?? ""
        if let data=defaults.data(forKey:"notchQuickTasks"),let saved=try? JSONDecoder().decode([QuickTask].self,from:data) { tasks=saved }
        else { tasks=[] }
        let duration=(defaults.object(forKey:"notchFocusDurationSeconds") as? Int) ?? NotchFocusTimer.defaultDurationSeconds
        let remaining=(defaults.object(forKey:"notchFocusRemainingSeconds") as? Int) ?? duration
        focusTimer=NotchFocusTimer(durationSeconds:duration,remainingSeconds:remaining)
        focusRemainingSeconds=focusTimer.remainingSeconds
        focusDurationSeconds=focusTimer.durationSeconds
    }

    func pointerEntered() {
        collapseWork?.cancel();collapseWork=nil
        interaction.pointerEntered();pointerInside=true
        setExpanded(interaction.isExpanded)
    }

    func pointerExited() {
        interaction.pointerExited();pointerInside=false
        scheduleCollapseIfAllowed()
    }

    func draggingEntered() {
        collapseWork?.cancel();collapseWork=nil
        interaction.draggingEntered();dropTargeted=true
        setExpanded(interaction.isExpanded)
    }

    func draggingExited() { dropTargeted=false }

    func draggingEnded() {
        interaction.draggingEnded();dropTargeted=false
        scheduleCollapseIfAllowed()
    }

    func acceptedDrop(_ urls:[URL],state:AppState) {
        guard !urls.isEmpty else { return }
        state.inputs=urls
        state.outputs=[]
        state.error=nil
        state.libraryVisible=false
        selectedTab = .drop
        pointerEntered()
        draggingEnded()
    }

    func select(_ tab:Tab) {
        selectedTab=tab
        if !isExpanded { expand() }
    }

    func toggleExpanded() {
        if isExpanded {
            collapseWork?.cancel();collapseWork=nil
            interaction.collapse()
            setExpanded(false)
        } else {
            expand()
        }
    }

    func expand() {
        collapseWork?.cancel();collapseWork=nil
        interaction.expand()
        setExpanded(true)
    }

    func stop() {
        collapseWork?.cancel();collapseWork=nil
        pauseFocusTimer()
        interaction.pointerExited();interaction.draggingEnded()
        dropTargeted=false;pointerInside=false
        setExpanded(false)
    }

    func addTask() {
        let title=taskDraft.trimmingCharacters(in:.whitespacesAndNewlines)
        guard !title.isEmpty else { return }
        tasks.append(QuickTask(title:title));taskDraft=""
    }

    func toggleTask(_ task:QuickTask) {
        guard let index=tasks.firstIndex(where:{$0.id==task.id}) else { return }
        tasks[index].isComplete.toggle()
    }

    func removeTask(_ task:QuickTask) { tasks.removeAll{$0.id==task.id} }

    func toggleFocusTimer() {
        if focusTimer.isRunning { pauseFocusTimer();return }
        if focusTimer.remainingSeconds==0 { focusTimer.restart() }
        focusTimer.start();publishFocusTimer()
        focusTick?.invalidate()
        focusTick=Timer.scheduledTimer(withTimeInterval:1,repeats:true) { [weak self] _ in
            DispatchQueue.main.async { self?.advanceFocusTimer() }
        }
    }

    func resetFocusTimer() {
        focusTick?.invalidate();focusTick=nil
        focusTimer.restart();publishFocusTimer()
    }

    func selectFocusDuration(minutes:Int) {
        guard NotchFocusTimer.presetMinutes.contains(minutes),!focusTimer.isRunning else { return }
        focusTick?.invalidate();focusTick=nil
        focusTimer.reset(durationSeconds:minutes*60);publishFocusTimer()
    }

    var focusTimeLabel:String {
        String(format:"%02d:%02d",focusRemainingSeconds/60,focusRemainingSeconds%60)
    }

    private func pauseFocusTimer() {
        focusTick?.invalidate();focusTick=nil
        focusTimer.pause();publishFocusTimer()
    }

    private func advanceFocusTimer() {
        let completed=focusTimer.tick();publishFocusTimer()
        if completed { focusTick?.invalidate();focusTick=nil }
    }

    private func publishFocusTimer() {
        focusRemainingSeconds=focusTimer.remainingSeconds
        focusDurationSeconds=focusTimer.durationSeconds
        focusTimerRunning=focusTimer.isRunning
        defaults.set(focusDurationSeconds,forKey:"notchFocusDurationSeconds")
        defaults.set(focusRemainingSeconds,forKey:"notchFocusRemainingSeconds")
    }

    private func persistTasks() {
        guard let data=try? JSONEncoder().encode(tasks) else { return }
        defaults.set(data,forKey:"notchQuickTasks")
    }

    private func scheduleCollapseIfAllowed() {
        guard interaction.mayCollapse else { return }
        collapseWork?.cancel()
        let work=DispatchWorkItem { [weak self] in
            guard let self else { return }
            if self.interaction.collapseIfAllowed() { self.setExpanded(false) }
        }
        collapseWork=work
        DispatchQueue.main.asyncAfter(deadline:.now()+.milliseconds(NotchInteractionPolicy.collapseDelayMilliseconds),execute:work)
    }

    private func setExpanded(_ expanded:Bool) {
        guard isExpanded != expanded else { return }
        isExpanded=expanded
        onExpandedChange?(expanded)
    }
}

struct NotchWorkspaceView: View {
    @ObservedObject var state:AppState
    @ObservedObject var model:NotchPanelModel
    @ObservedObject var media:FilePreview
    let onChooseFiles:()->Void
    let onChooseTool:(ToolKind)->Void
    @State private var isSeekingMedia=false
    @State private var mediaSeekPosition=0.0

    private var compactWidth:CGFloat {
        if state.working || media.mediaURL != nil { return 266 }
        if state.inputs.count==1 && !NotchDropShelfTargets.formats(for:state.inputs[0]).isEmpty { return 256 }
        return model.pointerInside ? 190:164
    }

    private var expandedHeadline:String {
        if state.working { return L10n.text("A file is on its way") }
        if model.selectedTab == .widgets { return L10n.text("A little space for focus") }
        return L10n.text("Your files, in flow")
    }

    var body: some View {
        Group {
            if model.isExpanded { expandedBody.transition(.opacity.combined(with:.scale(scale:0.96,anchor:.top))) }
            else { collapsedBody.transition(.opacity) }
        }
        .frame(width:model.isExpanded ? 540:compactWidth,height:model.isExpanded ? 470:44)
        .foregroundStyle(.white)
        .environment(\.colorScheme,.dark)
        .background(Color.black)
        .clipShape(NotchWorkspaceShape(inverseRadius:model.isExpanded ? 18:14,bottomRadius:model.isExpanded ? 24:22))
        .overlay(NotchWorkspaceShape(inverseRadius:model.isExpanded ? 18:14,bottomRadius:model.isExpanded ? 24:22).stroke(Color.white.opacity(0.13),lineWidth:1))
        .shadow(color:.black.opacity(0.35),radius:18,y:9)
        .animation(NashiMotion.enabled ? .spring(response:0.35,dampingFraction:0.78):nil,value:model.isExpanded)
        .animation(NashiMotion.enabled ? .easeOut(duration:0.16):nil,value:model.pointerInside)
        .onChange(of:media.mediaURL) { _ in isSeekingMedia=false;mediaSeekPosition=0 }
        .accessibilityElement(children:.contain)
    }

    private var collapsedBody: some View {
        HStack(spacing:10) {
            Button(action:model.toggleExpanded) { NashiMark(size:20) }.buttonStyle(.plain).accessibilityLabel(Text(L10n.text("Expand Nashi")))
            if state.working {
                VStack(alignment:.leading,spacing:3) {
                    Text(state.progressLabel.isEmpty ? "Working" : L10n.text(state.progressLabel))
                        .font(.system(size:10,weight:.semibold,design:.rounded)).lineLimit(1)
                    if state.progressIsDeterminate {
                        ProgressView(value:state.progress).tint(.white.opacity(0.9)).frame(width:110)
                    } else {
                        ProgressView().controlSize(.mini).tint(.white.opacity(0.9)).frame(width:110)
                    }
                }
                Spacer(minLength:0)
                Button { state.token.cancel() } label: { Image(systemName:"xmark.circle.fill") }
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Cancel")))
            } else if let mediaURL=media.mediaURL {
                Image(systemName:"waveform").font(.system(size:13,weight:.medium)).foregroundStyle(.white.opacity(0.68))
                Text(mediaURL.lastPathComponent).font(.system(size:10,weight:.medium,design:.rounded)).lineLimit(1).truncationMode(.middle)
                Spacer(minLength:2)
                Button { media.togglePlayback() } label: { Image(systemName:media.isPlaying ? "pause.fill":"play.fill") }
                    .buttonStyle(NotchIconButtonStyle())
                    .accessibilityIdentifier("NashiPreviewPlayPause")
                    .accessibilityLabel(Text(L10n.text(media.isPlaying ? "Pause Nashi preview":"Play Nashi preview")))
                Button(action:model.toggleExpanded) { Image(systemName:"chevron.down") }
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Expand Nashi")))
            } else if state.inputs.count==1,let format=NotchDropShelfTargets.formats(for:state.inputs[0]).first {
                Text(state.inputs[0].lastPathComponent).font(.system(size:10,weight:.medium,design:.rounded)).lineLimit(1).truncationMode(.middle)
                Spacer(minLength:2)
                Button {
                    state.select(.convert(format))
                } label: {
                    Text(format.uppercased()).font(.system(size:10,weight:.bold,design:.rounded)).padding(.horizontal,9).padding(.vertical,6)
                        .background(Color.white.opacity(0.13),in:Capsule())
                }.buttonStyle(.plain).accessibilityLabel(Text("Convert to \(format.uppercased())"))
            } else {
                Spacer(minLength:0)
                if model.pointerInside {
                    Button(action:onChooseFiles) { Image(systemName:"plus") }
                        .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Choose files")))
                }
                Button(action:model.toggleExpanded) { Image(systemName:"chevron.down") }
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Expand Nashi")))
            }
        }
        .padding(.horizontal,14)
        .frame(maxWidth:.infinity,maxHeight:.infinity)
        .contentShape(Rectangle())
    }

    private var expandedBody: some View {
        VStack(spacing:13) {
            HStack(spacing:10) {
                NashiMark(size:24)
                VStack(alignment:.leading,spacing:2) {
                    Text("NASHI").font(.system(size:9,weight:.bold,design:.rounded)).tracking(1.4).foregroundStyle(.white.opacity(0.55))
                    Text(expandedHeadline)
                        .font(.system(size:14,weight:.semibold,design:.rounded)).lineLimit(1)
                }
                Spacer()
                if state.working { compactProgress }
                Button(action:onChooseFiles) { Image(systemName:"plus") }
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Choose files")))
                Button(action:model.toggleExpanded) { Image(systemName:"chevron.up") }
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Collapse Nashi")))
            }

            if media.mediaURL != nil { mediaControlCard }

            HStack(spacing:5) {
                ForEach(NotchPanelModel.Tab.allCases) { tab in
                    Button { model.select(tab) } label: {
                        Label(L10n.text(tab.title),systemImage:tab.symbol)
                            .font(.system(size:11,weight:.semibold,design:.rounded))
                            .frame(maxWidth:.infinity,minHeight:34)
                            .foregroundStyle(model.selectedTab==tab ? Color.white:Color.white.opacity(0.62))
                            .background(model.selectedTab==tab ? Color.white.opacity(0.13):Color.clear,in:Capsule())
                    }
                    .buttonStyle(.plain)
                    .accessibilityAddTraits(model.selectedTab==tab ? .isSelected:[])
                }
            }
            .padding(4).background(Color.white.opacity(0.055),in:Capsule())

            Group {
                switch model.selectedTab {
                case .drop: dropShelf
                case .tools: toolsGrid
                case .history: historyAndProgress
                case .widgets: quickWidgets
                }
            }
            .frame(maxWidth:.infinity,maxHeight:.infinity,alignment:.topLeading)
        }
        .padding(.horizontal,18).padding(.top,16).padding(.bottom,17)
        .frame(maxWidth:.infinity,maxHeight:.infinity,alignment:.top)
        .accessibilityIdentifier("NashiNotchWorkspace")
    }

    private var compactProgress: some View {
        HStack(spacing:6) {
            if state.progressIsDeterminate { ProgressView(value:state.progress).progressViewStyle(.circular).tint(.white).frame(width:14,height:14) }
            else { ProgressView().controlSize(.mini).tint(.white).frame(width:14,height:14) }
            if state.progressIsDeterminate { Text("\(Int(state.progress*100))%").font(.system(size:10,weight:.semibold,design:.rounded)).monospacedDigit() }
        }
        .accessibilityElement(children:.combine).accessibilityLabel(Text(L10n.text("Conversion progress")))
    }

    private var mediaControlCard: some View {
        VStack(spacing:6) {
            HStack(spacing:9) {
                Image(systemName:"waveform").font(.system(size:13,weight:.medium)).foregroundStyle(.white.opacity(0.7))
                VStack(alignment:.leading,spacing:2) {
                    Text(L10n.text("Nashi media preview")).font(.system(size:9,weight:.semibold,design:.rounded)).foregroundStyle(.white.opacity(0.58))
                    Text(media.mediaURL?.lastPathComponent ?? "").font(.system(size:10,weight:.medium,design:.rounded)).lineLimit(1).truncationMode(.middle)
                }
                Spacer(minLength:4)
                Text(mediaTime(media.playhead)).font(.system(size:9,weight:.medium,design:.rounded)).monospacedDigit().foregroundStyle(.white.opacity(0.75))
                Button { media.seek(by:-10) } label: { Image(systemName:"gobackward.10") }
                    .buttonStyle(NotchIconButtonStyle())
                    .accessibilityIdentifier("NashiPreviewBack10")
                    .accessibilityLabel(Text(L10n.text("Go back 10 seconds in Nashi preview")))
                Button { media.togglePlayback() } label: { Image(systemName:media.isPlaying ? "pause.fill":"play.fill") }
                    .buttonStyle(NotchIconButtonStyle())
                    .accessibilityIdentifier("NashiPreviewPlayPause")
                    .accessibilityLabel(Text(L10n.text(media.isPlaying ? "Pause Nashi preview":"Play Nashi preview")))
                Button { media.seek(by:10) } label: { Image(systemName:"goforward.10") }
                    .buttonStyle(NotchIconButtonStyle())
                    .accessibilityIdentifier("NashiPreviewForward10")
                    .accessibilityLabel(Text(L10n.text("Go forward 10 seconds in Nashi preview")))
            }
            HStack(spacing:8) {
                Text(mediaTime(media.playhead)).font(.system(size:8,design:.rounded)).monospacedDigit().foregroundStyle(.white.opacity(0.48))
                Slider(value:Binding(get:{isSeekingMedia ? mediaSeekPosition:media.playhead},set:{mediaSeekPosition=$0}),in:0...max(media.duration,1),onEditingChanged:{ editing in
                    if editing { mediaSeekPosition=media.playhead;isSeekingMedia=true }
                    else { media.seek(to:mediaSeekPosition);isSeekingMedia=false }
                })
                .tint(.white)
                .disabled(media.duration<=0)
                .accessibilityIdentifier("NashiPreviewSeek")
                .accessibilityLabel(Text(L10n.text("Seek Nashi media preview")))
                Text(mediaTime(media.duration)).font(.system(size:8,design:.rounded)).monospacedDigit().foregroundStyle(.white.opacity(0.48))
            }
        }
        .padding(.horizontal,11).padding(.vertical,8)
        .background(Color.white.opacity(0.065),in:RoundedRectangle(cornerRadius:14,style:.continuous))
        .overlay(RoundedRectangle(cornerRadius:14,style:.continuous).strokeBorder(Color.white.opacity(0.09),lineWidth:1))
    }

    private func mediaTime(_ seconds:Double)->String {
        guard seconds.isFinite,seconds>=0 else { return "0:00" }
        let whole=Int(seconds),minutes=whole/60,hours=minutes/60
        return hours>0 ? String(format:"%d:%02d:%02d",hours,minutes%60,whole%60):String(format:"%d:%02d",minutes,whole%60)
    }

    private var dropShelf: some View {
        VStack(alignment:.leading,spacing:11) {
            HStack {
                VStack(alignment:.leading,spacing:3) {
                    Text(state.inputs.isEmpty ? "Drop files to get started":"Drop Shelf")
                        .font(.system(size:12,weight:.semibold,design:.rounded))
                    Text(state.inputs.isEmpty ? "Drop files here or choose them":"Choose a format to make a fresh copy")
                        .font(.system(size:10)).foregroundStyle(.white.opacity(0.56))
                }
                Spacer()
                if !state.inputs.isEmpty { Text("\(state.inputs.count) files").font(.system(size:10,weight:.medium,design:.rounded)).foregroundStyle(.white.opacity(0.6)) }
                if !state.inputs.isEmpty {
                    Button { state.perform(.float,urls:state.inputs) } label: {
                        Label(L10n.text("Add to Float"),systemImage:"rectangle.on.rectangle")
                    }
                    .buttonStyle(NotchCapsuleButtonStyle())
                    .accessibilityHint(Text(L10n.text("Keep these files together in a floating shelf")))
                }
                if state.inputs.isEmpty {
                    Button { state.pasteContent() } label: {
                        Label(L10n.text("Paste"),systemImage:"doc.on.clipboard")
                    }
                    .buttonStyle(NotchCapsuleButtonStyle())
                    .accessibilityIdentifier("NashiPasteFromClipboard")
                    .accessibilityHint(Text(L10n.text("Paste files, text, an image, or a link from the clipboard")))
                }
                Button(action:onChooseFiles) { Label(L10n.text("Choose files"),systemImage:"folder") }
                    .buttonStyle(NotchCapsuleButtonStyle()).accessibilityLabel(Text(L10n.text("Choose files")))
            }
            if state.inputs.isEmpty {
                VStack(spacing:9) {
                    Image(systemName:model.dropTargeted ? "arrow.down.to.line.compact":"tray.and.arrow.down")
                        .font(.system(size:23,weight:.light)).foregroundStyle(.white.opacity(0.72))
                    Text(model.dropTargeted ? "Release to add files":"Drop files here")
                        .font(.system(size:12,weight:.medium,design:.rounded))
                    Text("Originals stay untouched").font(.system(size:10)).foregroundStyle(.white.opacity(0.52))
                }
                .frame(maxWidth:.infinity,maxHeight:.infinity)
                .background(Color.white.opacity(model.dropTargeted ? 0.12:0.045),in:RoundedRectangle(cornerRadius:18,style:.continuous))
                .overlay(RoundedRectangle(cornerRadius:18,style:.continuous).strokeBorder(Color.white.opacity(model.dropTargeted ? 0.36:0.12),style:StrokeStyle(lineWidth:1,dash:[5,4])))
            } else {
                ScrollView(.horizontal) {
                    HStack(alignment:.top,spacing:9) {
                        ForEach(state.inputs,id:\.path) { url in DropShelfFileCard(url:url,state:state) }
                    }.padding(.vertical,2)
                }.scrollIndicators(.hidden)
            }
            if state.inputs.isEmpty {
                HStack(spacing:7) {
                    Image(systemName:"lock.fill").font(.system(size:9))
                    Text("Files stay on this Mac; Nashi writes a new output copy.").font(.system(size:10))
                }.foregroundStyle(.white.opacity(0.52))
            }
        }
        .accessibilityIdentifier("NashiDropShelf")
    }

    private var toolsGrid: some View {
        VStack(alignment:.leading,spacing:10) {
            HStack {
                Text("TOOLS").font(.system(size:9,weight:.bold,design:.rounded)).tracking(1.2).foregroundStyle(.white.opacity(0.55))
                Spacer()
                Text("\(ToolKind.allCases.count) tools").font(.system(size:10)).foregroundStyle(.white.opacity(0.55))
            }
            ScrollView {
                LazyVGrid(columns:Array(repeating:GridItem(.flexible(),spacing:7),count:5),spacing:7) {
                    ForEach(ToolKind.allCases,id:\.self) { tool in
                        let supported=state.inputs.isEmpty || FeatureCatalog.tools(for:state.inputs).contains(tool)
                        Button {
                            guard supported else { return }
                            if state.inputs.isEmpty { onChooseTool(tool) }
                            else { state.select(.tool(tool)) }
                        } label: {
                            VStack(spacing:7) {
                                Image(systemName:FeatureCatalog.toolSymbols[tool] ?? "square.grid.2x2")
                                    .font(.system(size:16,weight:.medium)).frame(height:20)
                                Text(L10n.text(FeatureCatalog.toolNames[tool] ?? tool.title))
                                    .font(.system(size:9,weight:.medium,design:.rounded)).multilineTextAlignment(.center).lineLimit(2).frame(height:24)
                            }
                            .foregroundStyle(supported ? Color.white:Color.white.opacity(0.28))
                            .frame(maxWidth:.infinity,minHeight:62)
                            .background(Color.white.opacity(supported ? 0.065:0.025),in:RoundedRectangle(cornerRadius:13,style:.continuous))
                        }
                        .buttonStyle(.plain).disabled(!supported)
                        .accessibilityLabel(Text(L10n.text(FeatureCatalog.toolNames[tool] ?? tool.title)))
                        .accessibilityHint(Text(L10n.text(supported ? "Open this Nashi tool":"Choose a compatible file first")))
                    }
                }.padding(.vertical,2)
            }.scrollIndicators(.hidden)
            if state.inputs.isEmpty { Text("Choose a tool, then select its file.").font(.system(size:10)).foregroundStyle(.white.opacity(0.55)) }
        }
        .accessibilityIdentifier("NashiToolsGrid")
    }

    private var historyAndProgress: some View {
        VStack(alignment:.leading,spacing:10) {
            if state.working {
                HStack(spacing:10) {
                    if state.progressIsDeterminate { ProgressView(value:state.progress).progressViewStyle(.circular).tint(.white).frame(width:22,height:22) }
                    else { ProgressView().controlSize(.small).tint(.white).frame(width:22,height:22) }
                    VStack(alignment:.leading,spacing:3) {
                        Text(L10n.text(state.activeTool.map { FeatureCatalog.toolNames[$0] ?? $0.title } ?? state.progressLabel))
                            .font(.system(size:12,weight:.semibold,design:.rounded)).lineLimit(1)
                        HStack(spacing:4) { if state.progressIsDeterminate { Text("\(Int(state.progress*100))%");Text("·") };Text("\(state.activeInputs.count) files") }
                            .font(.system(size:10)).foregroundStyle(.white.opacity(0.55))
                    }
                    Spacer()
                    Button { state.token.cancel() } label:{ Label(L10n.text("Cancel"),systemImage:"xmark") }
                        .buttonStyle(NotchCapsuleButtonStyle()).accessibilityLabel(Text(L10n.text("Cancel current operation")))
                }
                .padding(12).background(Color.white.opacity(0.075),in:RoundedRectangle(cornerRadius:15,style:.continuous))
            }
            HStack {
                Text("RECENT").font(.system(size:9,weight:.bold,design:.rounded)).tracking(1.2).foregroundStyle(.white.opacity(0.55))
                Spacer()
                Text("\(state.history.count)").font(.system(size:10,weight:.medium,design:.rounded)).foregroundStyle(.white.opacity(0.55))
            }
            if state.history.isEmpty && !state.working {
                VStack(spacing:9) {
                    Image(systemName:"clock.arrow.circlepath").font(.system(size:24,weight:.light))
                    Text("Nothing here yet").font(.system(size:12,weight:.semibold,design:.rounded))
                    Text("Completed files will appear here.").font(.system(size:10)).foregroundStyle(.white.opacity(0.55))
                }
                .foregroundStyle(.white.opacity(0.78)).frame(maxWidth:.infinity,maxHeight:.infinity)
            } else {
                ScrollView {
                    VStack(spacing:7) {
                        ForEach(state.history.prefix(8)) { job in NotchHistoryRow(job:job,state:state) }
                    }.frame(maxWidth:.infinity)
                }.scrollIndicators(.hidden)
            }
        }
        .accessibilityIdentifier("NashiHistoryProgress")
    }

    private var quickWidgets: some View {
        ScrollView {
            VStack(spacing:10) {
                focusTimerCard
                quickNoteCard
                taskListCard
                HStack(spacing:6) {
                    Image(systemName:"lock.fill").font(.system(size:9))
                    Text("Notes and tasks stay on this Mac.").font(.system(size:10))
                }
                .foregroundStyle(.white.opacity(0.48)).frame(maxWidth:.infinity,alignment:.leading)
            }
            .padding(.bottom,2)
        }
        .scrollIndicators(.hidden)
        .accessibilityIdentifier("NashiWidgets")
    }

    private var focusTimerCard: some View {
        VStack(spacing:9) {
            HStack(spacing:11) {
                ProgressView(value:Double(model.focusDurationSeconds-model.focusRemainingSeconds)/Double(model.focusDurationSeconds))
                    .progressViewStyle(.circular).tint(.white).frame(width:28,height:28)
                    .accessibilityLabel(Text(L10n.text("Focus timer progress")))
                VStack(alignment:.leading,spacing:3) {
                    Text("Focus timer").font(.system(size:12,weight:.semibold,design:.rounded))
                    Text(model.focusRemainingSeconds==0 ? L10n.text("Session complete"):L10n.text("A quiet {0}-minute session").replacingOccurrences(of:"{0}",with:String(model.focusDurationSeconds/60)))
                        .font(.system(size:9)).foregroundStyle(.white.opacity(0.53)).lineLimit(1)
                }
                Spacer(minLength:4)
                Text(model.focusTimeLabel).font(.system(size:17,weight:.semibold,design:.rounded)).monospacedDigit()
                Button(action:model.resetFocusTimer) { Image(systemName:"arrow.counterclockwise") }
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Reset focus timer")))
                Button(action:model.toggleFocusTimer) {
                    Label(L10n.text(model.focusTimerRunning ? "Pause":"Start"),systemImage:model.focusTimerRunning ? "pause.fill":"play.fill")
                        .font(.system(size:9,weight:.semibold,design:.rounded))
                        .padding(.horizontal,10).padding(.vertical,8)
                        .background(Color.white.opacity(0.12),in:Capsule())
                }
                .buttonStyle(.plain)
                .accessibilityLabel(Text(L10n.text(model.focusTimerRunning ? "Pause focus timer":"Start focus timer")))
            }
            HStack(spacing:5) {
                ForEach(NotchFocusTimer.presetMinutes,id:\.self) { minutes in
                    Button {
                        model.selectFocusDuration(minutes:minutes)
                    } label: {
                        Text("\(minutes)m")
                            .font(.system(size:9,weight:.semibold,design:.rounded).monospacedDigit())
                            .foregroundStyle(model.focusDurationSeconds==minutes*60 ? Color.black:Color.white.opacity(0.72))
                            .padding(.horizontal,8).padding(.vertical,5)
                            .background(model.focusDurationSeconds==minutes*60 ? Color.white:Color.white.opacity(0.08),in:Capsule())
                    }
                    .buttonStyle(.plain)
                    .disabled(model.focusTimerRunning)
                    .accessibilityLabel(Text(L10n.text("Set focus timer to {0} minutes").replacingOccurrences(of:"{0}",with:String(minutes))))
                    .accessibilityAddTraits(model.focusDurationSeconds==minutes*60 ? .isSelected:[])
                }
                Spacer(minLength:0)
            }
        }
        .padding(11)
        .background(Color.white.opacity(0.065),in:RoundedRectangle(cornerRadius:15,style:.continuous))
        .overlay(RoundedRectangle(cornerRadius:15,style:.continuous).strokeBorder(Color.white.opacity(0.09),lineWidth:1))
    }

    private var quickNoteCard: some View {
        VStack(alignment:.leading,spacing:7) {
            HStack(spacing:6) {
                Image(systemName:"note.text").font(.system(size:11))
                Text("Quick note").font(.system(size:11,weight:.semibold,design:.rounded))
                Spacer()
                Text("Saved on this Mac").font(.system(size:8)).foregroundStyle(.white.opacity(0.45))
            }
            ZStack(alignment:.topLeading) {
                if model.quickNote.isEmpty {
                    Text("Write a thought to keep nearby…")
                        .font(.system(size:10)).foregroundStyle(.white.opacity(0.34))
                        .padding(.leading,7).padding(.top,7)
                }
                TextEditor(text:$model.quickNote)
                    .font(.system(size:11,design:.rounded))
                    .scrollContentBackground(.hidden)
                    .background(Color.clear)
                    .accessibilityLabel(Text(L10n.text("Quick note")))
            }
            .frame(height:74)
            .padding(5)
            .background(Color.white.opacity(0.045),in:RoundedRectangle(cornerRadius:11,style:.continuous))
        }
        .padding(10)
        .background(Color.white.opacity(0.065),in:RoundedRectangle(cornerRadius:15,style:.continuous))
        .overlay(RoundedRectangle(cornerRadius:15,style:.continuous).strokeBorder(Color.white.opacity(0.09),lineWidth:1))
    }

    private var taskListCard: some View {
        VStack(alignment:.leading,spacing:8) {
            HStack(spacing:6) {
                Image(systemName:"checklist").font(.system(size:11))
                Text("To-do list").font(.system(size:11,weight:.semibold,design:.rounded))
                Spacer()
                Text("\(model.tasks.count)").font(.system(size:9,weight:.medium,design:.rounded)).foregroundStyle(.white.opacity(0.48))
            }
            HStack(spacing:7) {
                TextField(L10n.text("Add a task"),text:$model.taskDraft)
                    .textFieldStyle(.plain)
                    .font(.system(size:10,design:.rounded))
                    .padding(.horizontal,10).frame(height:31)
                    .background(Color.white.opacity(0.05),in:RoundedRectangle(cornerRadius:9,style:.continuous))
                    .accessibilityLabel(Text(L10n.text("Add a task")))
                    .onSubmit(model.addTask)
                Button(action:model.addTask) { Image(systemName:"plus") }
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Add task")))
                    .disabled(model.taskDraft.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty)
            }
            if model.tasks.isEmpty {
                Text("Your next small step goes here.")
                    .font(.system(size:9)).foregroundStyle(.white.opacity(0.43)).padding(.vertical,3)
            } else {
                ForEach(model.tasks.prefix(5)) { task in
                    HStack(spacing:8) {
                        Button { model.toggleTask(task) } label: {
                            Image(systemName:task.isComplete ? "checkmark.circle.fill":"circle")
                                .font(.system(size:13)).foregroundStyle(task.isComplete ? Color.white:Color.white.opacity(0.53))
                        }
                        .buttonStyle(.plain)
                        .accessibilityLabel(Text(L10n.text(task.isComplete ? "Mark task incomplete":"Mark task complete")+": "+task.title))
                        Text(task.title).font(.system(size:10,design:.rounded)).lineLimit(1)
                            .strikethrough(task.isComplete).foregroundStyle(.white.opacity(task.isComplete ? 0.48:0.88))
                        Spacer(minLength:0)
                        Button { model.removeTask(task) } label:{Image(systemName:"xmark")}
                            .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Remove task")+": "+task.title))
                    }
                }
            }
        }
        .padding(10)
        .background(Color.white.opacity(0.065),in:RoundedRectangle(cornerRadius:15,style:.continuous))
        .overlay(RoundedRectangle(cornerRadius:15,style:.continuous).strokeBorder(Color.white.opacity(0.09),lineWidth:1))
    }
}

private struct DropShelfFileCard:View {
    let url:URL
    @ObservedObject var state:AppState
    private var formats:[String] { NotchDropShelfTargets.formats(for:url) }

    var body: some View {
        VStack(alignment:.leading,spacing:9) {
            HStack(spacing:8) {
                AsyncFileArtwork(url:url).frame(width:34,height:34).padding(6)
                    .background(Color.white.opacity(0.08),in:RoundedRectangle(cornerRadius:11,style:.continuous))
                VStack(alignment:.leading,spacing:3) {
                    Text(url.lastPathComponent).font(.system(size:10,weight:.semibold,design:.rounded)).lineLimit(2).truncationMode(.middle)
                    Text(url.pathExtension.isEmpty ? L10n.text("File"):url.pathExtension.uppercased())
                        .font(.system(size:8,weight:.bold,design:.rounded)).tracking(0.8).foregroundStyle(.white.opacity(0.53))
                }
                Spacer(minLength:0)
                Button { state.inputs.removeAll{$0==url} } label:{Image(systemName:"xmark")}
                    .buttonStyle(NotchIconButtonStyle()).accessibilityLabel(Text(L10n.text("Remove")+": "+url.lastPathComponent))
                    .disabled(state.working)
            }
            Divider().overlay(Color.white.opacity(0.12))
            if formats.isEmpty {
                Text("No compatible conversion formats").font(.system(size:9)).foregroundStyle(.white.opacity(0.55)).frame(height:36,alignment:.center)
            } else {
                LazyVGrid(columns:Array(repeating:GridItem(.flexible(),spacing:5),count:4),spacing:5) {
                    ForEach(formats,id:\.self) { format in
                        Button {
                            guard !state.working else { return }
                            state.inputs=[url]
                            state.select(.convert(format))
                        } label:{ Text(format.uppercased()).font(.system(size:9,weight:.bold,design:.rounded)).frame(maxWidth:.infinity,minHeight:26) }
                            .buttonStyle(NotchFormatChipStyle()).disabled(state.working)
                            .accessibilityLabel(Text("Convert \(url.lastPathComponent) to \(format.uppercased())"))
                    }
                }
            }
        }
        .padding(11).frame(width:205,alignment:.topLeading)
        .background(Color.white.opacity(0.065),in:RoundedRectangle(cornerRadius:16,style:.continuous))
        .overlay(RoundedRectangle(cornerRadius:16,style:.continuous).strokeBorder(Color.white.opacity(0.1),lineWidth:1))
    }
}

private struct NotchHistoryRow:View {
    let job:JobRecord
    @ObservedObject var state:AppState
    private var urls:[URL] { job.outputs.map{URL(fileURLWithPath:$0)} }
    private var existing:[URL] { urls.filter{FileManager.default.fileExists(atPath:$0.path)} }

    var body: some View {
        HStack(spacing:9) {
            if let first=urls.first { AsyncFileArtwork(url:first).frame(width:28,height:28).padding(5).background(Color.white.opacity(0.07),in:RoundedRectangle(cornerRadius:9)) }
            VStack(alignment:.leading,spacing:3) {
                Text(urls.first?.lastPathComponent ?? L10n.text(job.title)).font(.system(size:10,weight:.semibold,design:.rounded)).lineLimit(1)
                Text(job.date.formatted(.relative(presentation:.named))).font(.system(size:9)).foregroundStyle(.white.opacity(0.5))
            }
            Spacer(minLength:4)
            Button { state.preview(job) } label:{Image(systemName:"eye")}.buttonStyle(NotchIconButtonStyle())
                .disabled(existing.isEmpty).accessibilityLabel(Text(L10n.text("Preview")))
            Button { NSWorkspace.shared.activateFileViewerSelecting(existing) } label:{Image(systemName:"folder")}.buttonStyle(NotchIconButtonStyle())
                .disabled(existing.isEmpty).accessibilityLabel(Text(L10n.text("Show in Finder")))
        }
        .padding(8).background(Color.white.opacity(0.05),in:RoundedRectangle(cornerRadius:13,style:.continuous))
    }
}

private struct NotchWorkspaceShape:Shape {
    let inverseRadius:CGFloat
    let bottomRadius:CGFloat

    func path(in rect:CGRect)->Path {
        let top=min(inverseRadius,min(rect.width/5,rect.height/3))
        let bottom=min(bottomRadius,min(rect.width/5,rect.height/3))
        var path=Path()
        path.move(to:CGPoint(x:rect.minX,y:rect.minY+top))
        path.addQuadCurve(to:CGPoint(x:rect.minX+top,y:rect.minY),control:CGPoint(x:rect.minX+top,y:rect.minY+top))
        path.addLine(to:CGPoint(x:rect.maxX-top,y:rect.minY))
        path.addQuadCurve(to:CGPoint(x:rect.maxX,y:rect.minY+top),control:CGPoint(x:rect.maxX-top,y:rect.minY+top))
        path.addLine(to:CGPoint(x:rect.maxX,y:rect.maxY-bottom))
        path.addQuadCurve(to:CGPoint(x:rect.maxX-bottom,y:rect.maxY),control:CGPoint(x:rect.maxX-bottom,y:rect.maxY-bottom))
        path.addLine(to:CGPoint(x:rect.minX+bottom,y:rect.maxY))
        path.addQuadCurve(to:CGPoint(x:rect.minX,y:rect.maxY-bottom),control:CGPoint(x:rect.minX+bottom,y:rect.maxY-bottom))
        path.closeSubpath()
        return path
    }
}

private struct NotchIconButtonStyle:ButtonStyle {
    func makeBody(configuration:Configuration)->some View {
        configuration.label.font(.system(size:11,weight:.semibold)).foregroundStyle(.white.opacity(configuration.isPressed ? 0.55:0.85))
            .frame(width:28,height:28).background(Color.white.opacity(configuration.isPressed ? 0.16:0.08),in:Circle())
            .contentShape(Circle())
    }
}

private struct NotchCapsuleButtonStyle:ButtonStyle {
    func makeBody(configuration:Configuration)->some View {
        configuration.label.font(.system(size:9,weight:.semibold,design:.rounded)).foregroundStyle(.white)
            .padding(.horizontal,10).padding(.vertical,7).background(Color.white.opacity(configuration.isPressed ? 0.18:0.11),in:Capsule())
    }
}

private struct NotchFormatChipStyle:ButtonStyle {
    func makeBody(configuration:Configuration)->some View {
        configuration.label.foregroundStyle(.white).background(Color.white.opacity(configuration.isPressed ? 0.24:0.13),in:Capsule())
    }
}
