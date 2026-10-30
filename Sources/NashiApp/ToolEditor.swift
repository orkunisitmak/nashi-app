import SwiftUI
import AppKit
import AVKit
import PDFKit
import NashiCore

struct ToolEditor:View {
    @ObservedObject var state:AppState
    let tool:ToolKind
    private let originalInputs:[URL]
    @State private var inputs:[URL]
    @MainActor init(state:AppState,tool:ToolKind) {
        self.state=state;self.tool=tool;originalInputs=state.inputs;_inputs=State(initialValue:state.inputs)
        if tool == .compress {
            let image=state.inputs.first.map{FileCategory.detect($0) == .image} ?? false
            let strength=image ? FormatPreferences.shared.imageCompressionStrength:FormatPreferences.shared.videoCompressionStrength
            _preset=State(initialValue:strength=="strong" ? "Strong":"Balanced")
            _compressionSize=State(initialValue:image ? FormatPreferences.shared.imageCompressionSize:"original")
        }
    }
    @State private var options=OperationOptions()
    @State private var image:NSImage?
    @State private var baseImage:CGImage?
    @State private var showOriginal=false
    @State private var previewSize:CGSize?
    @State private var channelPreviewURL:URL?
    @State private var waveform:[Float]=[]
    @State private var processedWaveform:[Float]?
    @State private var processedPlayback=false
    @State private var leftWaveform:[Float]=[]
    @State private var rightWaveform:[Float]=[]
    @State private var mediaPreviewFolder:URL?
    @State private var playbackURL:URL?
    @State private var previewBusy=false
    @State private var isLoading=true
    @State private var readyToApply=false
    @State private var loadRevision=UUID()
    @State private var editorGeneration=UUID()
    @State private var ownerWindowID:ObjectIdentifier?
    @State private var previewToken=CancellationToken()
    @State private var visualizationToken=CancellationToken()
    @State private var visualizationReady=false
    @State private var playingRange=false
    @State private var playerReachedEnd=false
    @State private var isPlaying=false
    @State private var seekRevision=UUID()
    @State private var seeking=false
    @State private var previewError:String?
    @State private var imageSize=CGSize(width:100,height:100)
    @State private var duration=0.0
    @State private var frameRate=30.0
    @State private var playhead=0.0
    @State private var player:AVPlayer?
    @State private var aspect="Free"
    @State private var cropInteracting=false
    @State private var width=0
    @State private var height=0
    @State private var targetEnabled=false
    @State private var targetMode="MB"
    @State private var targetValue=1.0
    @State private var preset="Balanced"
    @State private var compressionSize="original"
    @State private var annotationKind:ImageAnnotationKind = .arrow
    @State private var selectingAnnotations=false
    @State private var annotationText="Text"
    @State private var annotationColor=Color.red
    @State private var annotationStroke=3.0
    @State private var annotationFont=24.0
    @State private var annotationFontFamily="system"
    @State private var previewZoom:CGFloat=1
    @State private var metadata:[String:String]=[:]
    @State private var originalMetadata:[String:String]=[:]
    @State private var metadataSearch=""
    @State private var metadataRemoveAll=false
    @State private var addingMetadataField=false
    @State private var newMetadataKey="{XMP-dc}.Title"
    @State private var newMetadataValue=""
    @State private var selectedSplitClip:Int?
    @State private var processedDuration:Double?
    @State private var equalClips=2
    @State private var qrResults:[String]=[]
    @State private var pdfEditor=PDFPageEditorState()
    @Environment(\.dismiss) private var dismiss

    var input:URL? { inputs.first }
    var category:FileCategory { input.map(FileCategory.detect) ?? .other }
    var isGIFMetadata:Bool { input?.pathExtension.lowercased()=="gif" }
    var usesImageMetadata:Bool { category == .image || isGIFMetadata }
    var visual:Bool { [.editImage,.annotateImage,.cropImage,.frameImage,.redactImage,.createCollage].contains(tool) }
    var video:Bool { category == .video && ![.removeMetadata,.compress,.joinVideos].contains(tool) }
    var audio:Bool { category == .audio && tool != .removeMetadata && tool != .compress }
    var sideBySide:Bool { visual || video || audio }
    var body:some View {
        VStack(spacing:0) {
            HStack(spacing:14) {
                WindowCloseButton().frame(width:26,height:26)
                ZStack {
                    WindowDragRegion()
                    HStack(spacing:13) {
                        Image(systemName:FeatureCatalog.toolSymbols[tool] ?? "slider.horizontal.3").font(.system(size:19,weight:.medium)).foregroundStyle(Color.nashi).frame(width:44,height:44).background(NashiPalette.sand,in:RoundedRectangle(cornerRadius:14))
                        VStack(alignment:.leading,spacing:5) { LText(tool.title).font(.system(size:21,weight:.bold,design:.rounded)).tracking(-0.5);LText(toolHint).font(.system(size:11)).foregroundStyle(.secondary) }
                        Spacer()
                        NashiPill(title:inputs.count>1 ? "\(inputs.count) files":input?.pathExtension.uppercased() ?? "FILE",symbol:"doc")
                    }.allowsHitTesting(false)
                }
            }.padding(.horizontal,20).padding(.vertical,17).frame(height:82).uiAuditProbe("editor.header")
            Divider()
            HStack(spacing:0) {
                if sideBySide {
                    ScrollView {
                        VStack(alignment:.leading,spacing:18) {
                            HStack { Label("PREVIEW",systemImage:"viewfinder").font(.system(size:10,weight:.bold,design:.rounded)).tracking(1).foregroundStyle(.secondary);Spacer();if visual { Text("\(Int(imageSize.width)) × \(Int(imageSize.height))").font(.system(size:10,design:.monospaced)).foregroundStyle(.secondary) } }
                            if visual { imagePreview.disabled(isLoading || !readyToApply) }
                            if video || audio { mediaPreview.disabled(isLoading || !readyToApply) }
                            HStack(spacing:6) { Image(systemName:"doc");Text(input?.lastPathComponent ?? "").lineLimit(1) }.font(.system(size:11)).foregroundStyle(.secondary)
                        }.padding(24)
                    }.frame(minWidth:450,maxWidth:.infinity,maxHeight:.infinity).background(NashiPalette.inset.opacity(0.55)).uiAuditProbe("editor.preview")
                    Divider()
                }
                ScrollView {
                    VStack(alignment:.leading,spacing:18) {
                        Text(sideBySide ? L10n.text("MAKE IT YOURS"):"\(inputs.count>1 ? "\(inputs.count) FILES":input?.lastPathComponent ?? "")").font(.system(size:10,weight:.bold,design:.rounded)).tracking(0.8).foregroundStyle(.secondary).lineLimit(2)
                        if isLoading { ProgressView("Preparing your file…").font(.caption) }
                        controls.disabled(isLoading || !readyToApply)
                        if let previewError { HStack { LText(previewError).font(.callout).foregroundStyle(.orange);Spacer();Button { self.previewError=nil } label:{Image(systemName:"xmark")}.accessibilityLabel("Dismiss preview error") } }
                    }.padding(24).frame(maxWidth:sideBySide ? .infinity:720,alignment:.leading)
                }.frame(width:sideBySide ? 380:nil).frame(maxWidth:sideBySide ? nil:.infinity,maxHeight:.infinity).uiAuditProbe("editor.options")
            }.uiAuditProbe("editor.body")
            if state.activeTool==tool,state.activeInputs==inputs,(state.working || state.error != nil || (tool == .compress && !state.outputs.isEmpty)) {
                VStack(alignment:.leading,spacing:6) {
                    JobProgressView(state:state)
                    if tool == .compress,!state.working,!state.outputs.isEmpty {
                        LText(compressionResultText).font(.caption).foregroundStyle(.secondary)
                    }
                }.padding(.horizontal,20).padding(.vertical,10)
            }
            Divider()
            HStack(spacing:12) {
                Button { reset() } label:{Label("Reset",systemImage:"arrow.counterclockwise")}.buttonStyle(NashiButtonStyle(.quiet)).uiAuditProbe("editor.reset").accessibilityIdentifier("editor.reset")
                Spacer()
                Label("Original stays safe",systemImage:"lock").font(.system(size:10)).foregroundStyle(.secondary)
                Button { apply() } label:{Label { LText(primaryTitle) } icon:{ Image(systemName:"arrow.up.right") }.uiAuditProbe("editor.primary.label")}.buttonStyle(NashiButtonStyle(.primary)).uiAuditProbe("editor.primary").accessibilityIdentifier("editor.primary").disabled(!canApply).help(isCropTool ? "Save a cropped copy (Return or ⌘S)":"Save a fresh copy (⌘S)")
            }.padding(.horizontal,20).padding(.vertical,15).uiAuditProbe("editor.footer")
        }.frame(minWidth:sideBySide ? 900:480,minHeight:420).background(NashiPalette.canvas).tint(.nashi).buttonStyle(NashiButtonStyle(.secondary,compact:true)).textFieldStyle(.roundedBorder)
            .uiAuditStatus(isLoading ? "loading":readyToApply ? "ready":"failed: "+(previewError ?? "Input did not become ready"))
            .nashiAnimation(value:state.working).task(id:loadRevision) { await load() }
            .task(id:previewKey) { await renderPreview() }
            .task(id:visualizationKey) { await renderVisualization() }
            .onChange(of:previewKey) { _ in if processedPlayback { playOriginal(play:false) } }
            .background(WindowIdentityReader { ownerWindowID=$0 })
            .background(EditorKeyboardShortcuts(enabled:!isLoading && readyToApply && !state.working,onCommand:handleShortcut))
            .onReceive(NotificationCenter.default.publisher(for:NSWindow.willCloseNotification)) { notification in if let window=notification.object as? NSWindow,ownerWindowID==ObjectIdentifier(window) { disposeMedia() } }
            .onReceive(NotificationCenter.default.publisher(for:.AVPlayerItemDidPlayToEndTime).receive(on:DispatchQueue.main)) { notification in if let item=notification.object as? AVPlayerItem,item === player?.currentItem { playerReachedEnd=true } }
            .onChange(of:width) {
                if $0>0,tool == .editImage { options.width=$0 }
            }
            .onChange(of:height) {
                if $0>0,tool == .editImage { options.height=$0 }
            }
            .onChange(of:options.splitPoints) { _ in options.discardedClips=[];selectedSplitClip=nil }
            .onDeleteCommand { discardSelectedClip() }
            .onDisappear { disposeMedia() }
    }
    var toolHint:String {
        switch tool {
        case .cropImage,.cropVideo:return "Keep the good part."
        case .editImage:return "Little tweaks. A whole new look."
        case .annotateImage:return "Make your point."
        case .frameImage:return "Give it a little breathing room."
        case .compress:return "Make some space. Keep what matters."
        case .removeMetadata:return "Choose what your file shares."
        case .createCollage:return "Better together."
        case .trimAudio,.trimVideo:return "Straight to the good stuff."
        default:return "Fine-tune it. Save a fresh copy."
        }
    }
    var primaryTitle:String {
        switch tool {
        case .editImage:return "Save edited copy"
        case .annotateImage:return "Save annotated copy"
        case .frameImage:return "Save with background"
        case .redactImage,.redactVideo:return "Save redacted copy"
        case .redactAudio:return options.audioMode == "bleep" ? "Save bleeped copy":"Save muted copy"
        case .audioToVideo:return "Create visualization"
        case .removeMetadata:return "Save metadata copy"
        case .videoSnapshots:return "Save \(options.snapshotTimes.count) frame\(options.snapshotTimes.count==1 ? "":"s")"
        case .cropImage,.cropVideo:return "Save cropped copy"
        case .trimAudio,.trimVideo:return "Save trimmed copy"
        case .changeVideoSpeed:return "Save speed copy"
        case .audioChannels:return "Save audio copy"
        case .splitVideo:return options.joinSplitClips ? "Export one video":"Export clips"
        case .organizePDF:return "Save organized PDF"
        case .compress:return "Compress & save"
        default:return FeatureCatalog.toolNames[tool] ?? tool.title
        }
    }
    var canApply:Bool {
        guard !state.working,!isLoading,readyToApply,input != nil else { return false }
        switch tool {
        case .cropImage,.cropVideo:
            guard !cropInteracting,let crop=options.crop else { return false }
            return crop.x>=0 && crop.y>=0 && crop.width>=1 && crop.height>=1 && crop.x+crop.width<=imageSize.width && crop.y+crop.height<=imageSize.height
        case .removeMetadata where isGIFMetadata:return metadataRemoveAll
        case .splitVideo:return (!options.splitPoints.isEmpty || options.equalClips != nil) && options.discardedClips.count<splitIntervals.count
        case .videoSnapshots:return !options.snapshotTimes.isEmpty
        case .annotateImage:return !options.imageAnnotations.isEmpty
        case .redactImage,.redactVideo:return !options.redactions.isEmpty
        case .redactAudio:return !options.audioRedactions.isEmpty
        default:return true
        }
    }
    @ViewBuilder var controls:some View {
        switch tool {
        case .compress: compressionControls
        case .cropImage,.cropVideo: cropControls
        case .editImage: imageControls
        case .annotateImage: annotationControls
        case .frameImage: frameControls
        case .createCollage: collageControls
        case .redactImage,.redactVideo: redactionControls
        case .trimVideo,.trimAudio: trimControls
        case .changeVideoSpeed:
            slider("Playback speed",value:$options.speed,range:0.25...4,unit:"×")
            HStack { ForEach([0.25,0.5,1,1.5,2,4],id:\.self) { speed in Button("\(speed, specifier:"%.2g")×") { options.speed=speed;player?.currentItem?.audioTimePitchAlgorithm = .spectral;if playerReachedEnd { seek(0) };player?.rate=Float(speed) }.buttonStyle(.bordered) } }
            Text("Audio pitch is preserved.").font(.caption).foregroundStyle(.secondary)
        case .normalizeAudio:
            slider("Target loudness",value:$options.targetLoudness,range:-40 ... -5,unit:"LUFS")
            slider("Loudness range",value:$options.loudnessRange,range:1...20,unit:"LU")
            slider("True peak limit",value:$options.truePeak,range:-9 ... -0.1,unit:"dBTP")
        case .audioChannels:
            Picker("Output",selection:$options.channels) { Text("Mono").tag(1);Text("Stereo").tag(2) }.pickerStyle(.segmented)
            slider("Left volume",value:$options.leftVolume,range:0...2,unit:"×");slider("Right volume",value:$options.rightVolume,range:0...2,unit:"×")
            HStack { Button("Preview Left") { previewChannel(0) };Button("Preview Right") { previewChannel(1) };Button("Original") { playOriginal() } }
        case .splitVideo:
            HStack { Text("\(options.splitPoints.count) split point\(options.splitPoints.count==1 ? "":"s")");Spacer();Button("Add Split",action:addSplitPoint).disabled(!canAddSplitPoint).help("Add split at playhead (M)") }
            ForEach(Array(options.splitPoints.enumerated()),id:\.offset) { index,time in HStack { Text(timeLabel(time));Spacer();Button { options.equalClips=nil;options.splitPoints.remove(at:index) } label:{Image(systemName:"xmark.circle")} } }
            HStack { Stepper("Equal clips: \(equalClips)",value:$equalClips,in:2...100);Button("Set") { options.equalClips=equalClips;options.splitPoints=(1..<equalClips).map{duration*Double($0)/Double(equalClips)} } }
            splitEditingControls
        case .videoSnapshots:
            HStack { Text("\(options.snapshotTimes.count) selected");Spacer();Button("Add Frame") { options.snapshotTimes.append(playhead) }.help("Add frame at playhead (M)") }
            if let source=playbackURL ?? input { LazyVGrid(columns:[GridItem(.adaptive(minimum:100))]) { ForEach(Array(options.snapshotTimes.enumerated()),id:\.offset) { index,time in VStack {
                VideoFrameThumbnail(source:source,time:time).frame(height:70).cornerRadius(6)
                HStack { Text(timeLabel(time)).font(.caption);Spacer();Button { options.snapshotTimes.remove(at:index) } label:{Image(systemName:"xmark.circle")} }
            } } } }
        case .audioToVideo:
            Picker("Style",selection:$options.visualizationStyle) { Text("Waveform").tag("waveform");Text("Spectrum").tag("spectrum");Text("Bars").tag("bars");Text("Still image").tag("image") }
            if options.visualizationStyle == "image" { Button("Choose Still Image…") { let p=NSOpenPanel();if p.runModal() == .OK { options.visualizationImage=p.url } } }
            HStack { Text("Video size");Spacer();Button("Landscape") { width=1920;height=1080 };Button("Portrait") { width=1080;height=1920 };Button("Square") { width=1080;height=1080 } };dimensionFields
            if options.visualizationStyle == "image" { ColorPicker("Background",selection:colorBinding($options.backgroundColor)) }
        case .redactAudio:
            Picker("Style",selection:$options.audioMode) { Text("Bleep").tag("bleep");Text("Mute").tag("mute") }.pickerStyle(.segmented)
            timeRangeFields
            Button("Add Redaction") { options.audioRedactions.append(TimeRange(start:options.start,end:options.end ?? duration)) }
            ForEach(Array(options.audioRedactions.enumerated()),id:\.offset) { index,range in HStack { Text("\(timeLabel(range.start)) → \(timeLabel(range.end))");Spacer();Button { options.audioRedactions.remove(at:index) } label:{Image(systemName:"xmark.circle")} } }
        case .joinVideos,.mergePDF: orderControls
        case .organizePDF: pdfControls
        case .removeMetadata: metadataControls
        case .splitPDF:
            Stepper("Pages per file: \(options.pagesPerFile)",value:$options.pagesPerFile,in:1...1000)
            TextField("Page ranges, for example 1-3, 5",text:$options.pageRanges)
        default: EmptyView()
        }
    }
    var compressionControls:some View {
        VStack(alignment:.leading,spacing:18) {
            if let input,let size=try? input.resourceValues(forKeys:[.fileSizeKey]).fileSize { LText("Original size: \(ByteCountFormatter.string(fromByteCount:Int64(size),countStyle:.file))").fontWeight(.semibold) }
            Text("Compression preset").font(.headline)
            Picker("Preset",selection:$preset) { Text("Balanced").tag("Balanced");Text("Strong").tag("Strong") }.pickerStyle(.segmented).labelsHidden()
            Text(preset=="Balanced" ? "Smaller files with less quality loss.":"Smaller files with stronger compression.").foregroundStyle(.secondary).font(.callout)
            if category == .image {
                Picker("Image size",selection:$compressionSize) {
                    Text("Keep original dimensions").tag("original");Text("75%").tag("75");Text("50%").tag("50");Text("25%").tag("25");Text("Custom dimensions").tag("custom")
                    Text("Large · 2560 px longest edge").tag("large");Text("Medium · 1920 px longest edge").tag("medium");Text("Small · 1280 px longest edge").tag("small")
                }.onChange(of:compressionSize) { value in
                    setCompressionDimensions(value)
                }
                if compressionSize == "custom" { dimensionFields }
            }
            Divider();Toggle("Compress to target file size",isOn:$targetEnabled)
            if targetEnabled { HStack { TextField("Target",value:$targetValue,format:.number);Picker("Unit",selection:$targetMode) { Text("MB").tag("MB");Text("KB").tag("KB");Text("% reduction").tag("percent") }.frame(width:150) } }
        }
    }
    var cropControls:some View {
        VStack(alignment:.leading,spacing:14) {
            Text("Drag across the preview to select the area to keep.").font(.caption).foregroundStyle(.secondary)
            Text("Shift locks the ratio · Return crops\nDrag inside to move; drag a handle to resize.").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
            if tool == .cropImage,inputs.count>1 {
                Text("This crop applies to every selected image and must fit inside each one.").font(.caption).foregroundStyle(.secondary)
            }
            Picker("Aspect ratio",selection:$aspect) { ForEach(["Free","1:1","16:9","9:16","4:3","3:4"],id:\.self) { LText($0) } }.pickerStyle(.segmented).onChange(of:aspect) { value in
                guard value != "Free" else { return };let a=value.split(separator:":").compactMap{Double($0)};if a.count==2 { let ratio=a[0]/a[1];let w=min(imageSize.width,imageSize.height*ratio),h=w/ratio;setCrop(EditRect(x:(imageSize.width-w)/2,y:(imageSize.height-h)/2,width:w,height:h)) }
            }
            HStack { number("W",value:cropDimension(width:true));number("H",value:cropDimension(width:false));Text("px").foregroundStyle(.secondary) }
            if let crop=options.crop { HStack { number("X",value:Binding(get:{Int(crop.x)},set:{options.crop?.x=Double($0)}));number("Y",value:Binding(get:{Int(crop.y)},set:{options.crop?.y=Double($0)})) } }
            HStack {
                Button("Select All") { setCrop(EditRect(x:0,y:0,width:imageSize.width,height:imageSize.height));aspect="Free" }
                Button("Clear Selection") { options.crop=nil;width=Int(imageSize.width);height=Int(imageSize.height) }.disabled(options.crop==nil)
            }
        }
    }
    var isCropTool:Bool { tool == .cropImage || tool == .cropVideo }
    var cropAspectRatio:CGFloat? {
        let parts=aspect.split(separator:":").compactMap{Double($0)}
        return parts.count==2 ? parts[0]/parts[1]:nil
    }
    func cropDimension(width isWidth:Bool)->Binding<Int> {
        Binding(get:{isWidth ? width:height},set:{value in
            var rect=options.crop ?? EditRect(x:0,y:0,width:imageSize.width,height:imageSize.height)
            if isWidth { rect.width=Double(value);if let ratio=cropAspectRatio { rect.height=rect.width/ratio } }
            else { rect.height=Double(value);if let ratio=cropAspectRatio { rect.width=rect.height*ratio } }
            setCrop(rect)
        })
    }
    func cropInteraction(_ active:Bool) {
        cropInteracting=active
        if active,let window=NSApp.keyWindow,ownerWindowID==ObjectIdentifier(window) { window.makeFirstResponder(nil) }
    }
    var dimensionFields:some View { HStack { number("W",value:$width);number("H",value:$height);Text("px").foregroundStyle(.secondary) } }
    var imageControls:some View {
        VStack(alignment:.leading,spacing:12) {
            HStack { Toggle("Show Original",isOn:$showOriginal).toggleStyle(.button);Spacer();Button("−") { previewZoom=max(0.25,previewZoom/1.25) };Text("\(Int(previewZoom*100))%").font(.caption.monospacedDigit());Button("+") { previewZoom=min(4,previewZoom*1.25) };Button("Fit") { previewZoom=1 } }
            HStack { Button("Rotate Left") { options.rotation-=90;swap(&width,&height) };Button("Rotate Right") { options.rotation+=90;swap(&width,&height) };Toggle("Flip",isOn:$options.flipHorizontal) }
            dimensionFields
            Group {
                Text("Lighting").font(.headline).foregroundStyle(.secondary)
                slider("Exposure",value:$options.imageAdjustments.exposure,range:-3...3,unit:"EV")
                slider("Brightness",value:$options.imageAdjustments.brightness,range:-1...1)
                slider("Contrast",value:relative($options.imageAdjustments.contrast),range:-1...1)
                slider("Highlights",value:$options.imageAdjustments.highlights,range:-1...1)
                slider("Shadows",value:$options.imageAdjustments.shadows,range:-1...1)
                slider("Whites",value:$options.imageAdjustments.whites,range:-1...1)
                slider("Blacks",value:$options.imageAdjustments.blacks,range:-1...1)
                slider("Gamma",value:relative($options.imageAdjustments.gamma),range:-0.8...2)
            }
            Group {
                Text("Color").font(.headline).foregroundStyle(.secondary)
                slider("Temperature",value:$options.imageAdjustments.temperature,range:-1...1)
                slider("Tint",value:$options.imageAdjustments.tint,range:-1...1)
                slider("Saturation",value:relative($options.imageAdjustments.saturation),range:-1...1)
                slider("Vibrance",value:$options.imageAdjustments.vibrance,range:-1...1)
                slider("Hue",value:$options.imageAdjustments.hue,range:-180...180,unit:"°")
            }
            Group {
                Text("Detail").font(.headline).foregroundStyle(.secondary)
                slider("Clarity",value:$options.imageAdjustments.clarity,range:0...1)
                slider("Texture",value:$options.imageAdjustments.texture,range:-1...1)
                slider("Sharpen",value:$options.imageAdjustments.sharpen,range:0...2)
                slider("Dehaze",value:$options.imageAdjustments.dehaze,range:0...1)
                slider("Grain",value:$options.imageAdjustments.grain,range:0...1)
                slider("Noise reduction",value:$options.imageAdjustments.noiseReduction,range:0...1)
                slider("Vignette",value:$options.imageAdjustments.vignette,range:0...2)
                slider("Fade",value:$options.imageAdjustments.fade,range:0...1)
                slider("Blur",value:$options.imageAdjustments.blur,range:0...30,unit:"px")
            }
        }
    }
    var annotationControls:some View {
        VStack(alignment:.leading,spacing:12) {
            Picker("Tool",selection:$annotationKind) { ForEach(ImageAnnotationKind.allCases,id:\.self) { LText($0.rawValue.capitalized).tag($0) } }.onChange(of:annotationKind) { _ in selectingAnnotations=false }
            Toggle(isOn:$selectingAnnotations) { Label("Select / Move",systemImage:"cursorarrow") }.toggleStyle(.button)
            TextField("Text or callout",text:$annotationText);ColorPicker("Color",selection:$annotationColor)
            Picker("Font",selection:$annotationFontFamily) { Text("System").tag("system");Text("Rounded").tag("rounded");Text("Serif").tag("serif");Text("Monospaced").tag("monospaced") }
            slider("Stroke width",value:$annotationStroke,range:1...40,unit:"px");slider("Text size",value:$annotationFont,range:8...160,unit:"px")
            LText(selectingAnnotations ? "Drag an existing annotation to move it; drag its handles to resize. Turn off Select / Move to draw.":"Drag to draw with the selected tool. Use Select / Move to edit an existing annotation.").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
            HStack { Button("Undo") { if !options.imageAnnotations.isEmpty { options.imageAnnotations.removeLast() } };Button("Clear") { options.imageAnnotations=[] } }
            ForEach(Array(options.imageAnnotations.enumerated()),id:\.offset) { index,a in HStack { Text("\(index+1). \(a.kind.rawValue.capitalized)");Spacer();Button("Delete") { options.imageAnnotations.remove(at:index) } } }
        }
    }
    var frameControls:some View {
        VStack(alignment:.leading,spacing:14) {
            Picker("Background",selection:frame(\.backdrop)) { ForEach(ImageBackdrop.allCases,id:\.self) { LText($0.rawValue.capitalized).tag($0) } }
            if options.imageFrame?.backdrop == .gradient { HStack { ForEach(0..<3) { index in
                let colors=[["#FF6B79","#FFD85C","#895CE0"],["#4B79A1","#283E51"],["#A8FF78","#78FFD6"]][index]
                Button { var f=options.imageFrame ?? .init();f.backdrop = .gradient;f.gradientColors=colors;options.imageFrame=f } label:{RoundedRectangle(cornerRadius:6).fill(LinearGradient(colors:colors.map{Color(cgColor:ImageEngine.color($0))},startPoint:.topLeading,endPoint:.bottomTrailing)).frame(width:60,height:36)}.buttonStyle(.plain)
            } } }
            ColorPicker("Color",selection:colorBinding(frame(\.backgroundColor),preservingOpacity:true))
            if ImageEngine.color(options.imageFrame?.backgroundColor ?? "#FFFFFF").alpha < 1 {
                Text("Transparent backgrounds save as PNG to keep their transparency.").font(.caption).foregroundStyle(.secondary)
            }
            Button("Choose Background Image…") { let p=NSOpenPanel();if p.runModal() == .OK { var f=options.imageFrame ?? .init();f.backgroundImage=p.url;f.backdrop = .image;options.imageFrame=f } }
            slider("Blur",value:frame(\.backgroundBlur),range:0...60)
            slider("Padding",value:Binding(get:{Double(options.imageFrame?.padding ?? 160)},set:{var f=options.imageFrame ?? .init();f.padding=Int($0);options.imageFrame=f}),range:0...400,unit:"px")
            slider("Corners",value:frame(\.cornerRadius),range:0...100,unit:"px")
            slider("Shadow",value:frame(\.shadowRadius),range:0...80,unit:"px")
            HStack { ForEach(["Auto","1:1","16:9","4:5"],id:\.self) { label in Button(label) { var f=options.imageFrame ?? .init();let a=label.split(separator:":").compactMap{Double($0)};f.aspectRatio=a.count==2 ? a[0]/a[1]:nil;options.imageFrame=f } } }
        }
    }
    var collageControls:some View {
        VStack(alignment:.leading,spacing:14) {
            Picker("Style",selection:collage(\.layout)) { ForEach(CollageLayout.allCases,id:\.self) { LText($0.rawValue.capitalized).tag($0) } }
            Stepper("Columns: \(options.collage?.columns ?? 2)",value:collage(\.columns),in:1...12)
            Picker("Canvas",selection:collage(\.aspectRatio)) { Text("Square · 1:1").tag(1.0);Text("Landscape · 16:9").tag(16.0/9);Text("Portrait · 9:16").tag(9.0/16);Text("Portrait · 4:5").tag(4.0/5) }
            number("Canvas width",value:collage(\.width))
            Picker("Image fit",selection:collage(\.imageFit)) { Text("Fill cells (crop edges)").tag(ImageFit.fill);Text("Fit whole image").tag(ImageFit.fit) }
            number("Spacing",value:collage(\.spacing));number("Outer padding",value:collage(\.outerPadding));slider("Corners",value:collage(\.cornerRadius),range:0...100,reset:CollageOptions().cornerRadius)
            ColorPicker("Background",selection:colorBinding(collage(\.backgroundColor),preservingOpacity:true))
            HStack { Button("Background Image…") { let panel=NSOpenPanel();if panel.runModal() == .OK { var c=options.collage ?? .init();c.backgroundImage=panel.url;options.collage=c } };if options.collage?.backgroundImage != nil { Button("Remove Image") { var c=options.collage ?? .init();c.backgroundImage=nil;options.collage=c } } }
            orderControls
        }
    }
    var redactionControls:some View {
        VStack(alignment:.leading,spacing:12) {
            Picker("Style",selection:$options.redactionMode) { Text("Solid").tag("black");Text("Blur").tag("blur");if tool == .redactImage { Text("Pixelate").tag("pixelate") } }.pickerStyle(.segmented)
            ColorPicker("Color",selection:colorBinding($options.backgroundColor),supportsOpacity:false)
            Text("Drag on the preview to add an area. Each area is saved into the new copy.").font(.caption).foregroundStyle(.secondary)
            if tool == .redactImage { HStack { Button("Detect Faces") { detectRedactions(faces:true) };Button("Detect Text") { detectRedactions(faces:false) } } }
            if tool == .redactVideo { timeRangeFields }
            Text("\(options.redactions.count) area\(options.redactions.count==1 ? "":"s") selected")
            if tool == .redactVideo { ForEach(Array(options.videoRedactions.enumerated()),id:\.offset) { index,redaction in HStack {
                LText("Area \(index+1)");TextField("Start",value:Binding(get:{options.videoRedactions[index].start},set:{options.videoRedactions[index].start=$0}),format:.number)
                TextField("End",value:Binding(get:{options.videoRedactions[index].end ?? duration},set:{options.videoRedactions[index].end=$0}),format:.number)
            } } }
            Button("Undo Area") { if !options.redactions.isEmpty { options.redactions.removeLast() };if tool == .redactVideo,!options.videoRedactions.isEmpty { options.videoRedactions.removeLast() } }
        }
    }
    var trimControls:some View {
        VStack(spacing:14) { timeRangeFields;if tool == .trimAudio { Toggle("Remove silence from beginning and end",isOn:$options.trimSilence);if options.trimSilence { slider("Silence threshold",value:$options.silenceThresholdDb,range:-80 ... -10,unit:"dB") } };LText("Selected duration: \(timeLabel(max(0,(options.end ?? duration)-options.start)))").font(.caption).foregroundStyle(.secondary) }
    }
    var timeRangeFields:some View {
        HStack { Text("Start");TextField("Seconds",value:$options.start,format:.number.precision(.fractionLength(3)));Text("End");TextField("Seconds",value:Binding(get:{options.end ?? duration},set:{options.end=$0}),format:.number.precision(.fractionLength(3))) }.textFieldStyle(.roundedBorder)
    }
    var splitIntervals:[TimeRange] {
        let points=[0]+options.splitPoints.filter{$0>0 && $0<duration}.sorted()+[duration]
        return zip(points,points.dropFirst()).map{TimeRange(start:$0.0,end:$0.1)}
    }
    var splitEditingControls:some View {
        VStack(alignment:.leading,spacing:12) {
            Text("Select a clip and press Delete to remove it from the result.").font(.caption).foregroundStyle(.secondary)
            ScrollView(.horizontal,showsIndicators:false) {
                HStack(spacing:5) { ForEach(Array(splitIntervals.enumerated()),id:\.offset) { index,range in
                    Button { selectedSplitClip=index;if processedPlayback { playOriginal(play:false) };seek(range.start) } label:{
                        VStack(spacing:4) { LText("Clip \(index+1)").font(.caption.bold());Text(timeLabel(range.end-range.start)).font(.caption2.monospacedDigit()) }.frame(width:max(65,300*(range.end-range.start)/max(0.001,duration)),height:46).strikethrough(options.discardedClips.contains(index)).foregroundStyle(options.discardedClips.contains(index) ? Color.secondary:Color.primary).background(selectedSplitClip==index ? Color.nashi.opacity(0.3):Color.primary.opacity(0.08)).cornerRadius(6)
                    }.buttonStyle(.plain).opacity(options.discardedClips.contains(index) ? 0.4:1)
                } }.focusable()
            }
            HStack { Button("Delete Clip") { discardSelectedClip() }.disabled(selectedSplitClip==nil || splitIntervals.count-options.discardedClips.count<=1);Button("Restore Clips") { options.discardedClips=[] }.disabled(options.discardedClips.isEmpty);Spacer() }
            HStack { Button(previewBusy ? "Preparing…":"Play Result") { processedPreview() }.disabled(previewBusy || !canApply);Button("Export One Video") { apply(joinedSplit:true) }.disabled(!canApply) }
        }
    }
    func discardSelectedClip() {
        guard tool == .splitVideo,let selectedSplitClip,splitIntervals.indices.contains(selectedSplitClip),!options.discardedClips.contains(selectedSplitClip),splitIntervals.count-options.discardedClips.count>1 else { return }
        options.discardedClips.append(selectedSplitClip)
    }
    func setCompressionDimensions(_ value:String) {
        guard value != "custom" else { return }
        let maximum=["large":2560.0,"medium":1920.0,"small":1280.0][value]
        let factor:Double
        if let maximum { factor=min(1,maximum/Double(max(imageSize.width,imageSize.height))) }
        else { factor=value=="original" ? 1:(Double(value) ?? 100)/100 }
        width=max(1,Int(imageSize.width*factor));height=max(1,Int(imageSize.height*factor))
    }
    var orderControls:some View {
        VStack(alignment:.leading,spacing:12) { Text("Playback / document order").font(.headline);ForEach(Array(inputs.enumerated()),id:\.offset) { index,url in HStack {
            Text("\(index+1)").foregroundStyle(.secondary).frame(width:20);Image(nsImage:NSWorkspace.shared.icon(forFile:url.path)).resizable().frame(width:28,height:28);Text(url.lastPathComponent).lineLimit(1);Spacer()
            Button { inputs.swapAt(index,index-1) } label:{Image(systemName:"chevron.up")}.disabled(index==0)
            Button { inputs.swapAt(index,index+1) } label:{Image(systemName:"chevron.down")}.disabled(index==inputs.count-1)
        } } }
    }
    var pdfControls:some View {
        VStack(alignment:.leading,spacing:14) {
            LazyVGrid(columns:[GridItem(.adaptive(minimum:110))],spacing:14) { ForEach(Array(pdfEditor.pages.enumerated()),id:\.offset) { index,page in VStack { Image(nsImage:page.thumbnail(of:NSSize(width:100,height:140),for:.cropBox)).resizable().scaledToFit().frame(height:140);Text("\(index+1)").font(.caption) }.padding(8).background(pdfEditor.selectedPage==index ? Color.nashi.opacity(0.2):Color.primary.opacity(0.03)).cornerRadius(8).onTapGesture{pdfEditor.select(index)} } }
            HStack {
                Button("Rotate") { pdfEditor.rotateSelected() }.disabled(!pdfEditor.hasSelection)
                Button("Duplicate") { pdfEditor.duplicateSelected() }.disabled(!pdfEditor.hasSelection)
                Button("Delete") { pdfEditor.deleteSelected() }.disabled(!pdfEditor.canDelete)
                Button("Move Left") { pdfEditor.moveSelected(by:-1) }.disabled(!pdfEditor.canMoveLeft)
                Button("Move Right") { pdfEditor.moveSelected(by:1) }.disabled(!pdfEditor.canMoveRight)
            }
            Toggle("Make all pages the same width",isOn:$options.pdfUniformWidth)
        }
    }
    var metadataControls:some View {
        VStack(alignment:.leading,spacing:12) {
            TextField("Search metadata",text:$metadataSearch)
            ForEach(metadataGroups,id:\.self) { group in
                VStack(alignment:.leading,spacing:8) {
                    LText(group).font(.headline).foregroundStyle(.secondary)
                    ForEach(visibleMetadata.filter{metadataGroup($0)==group},id:\.self) { key in HStack(alignment:.top) {
                        Text(key).font(.caption).frame(width:130,alignment:.leading).textSelection(.enabled)
                        if isGIFMetadata || (usesImageMetadata && !ImageMetadataEngine.isWritable(key)) { Text(metadata[key] ?? "").frame(maxWidth:.infinity,alignment:.leading).textSelection(.enabled) }
                        else { TextField("Value",text:Binding(get:{metadata[key] ?? ""},set:{metadata[key]=$0})).textFieldStyle(.roundedBorder)
                            Button { metadata.removeValue(forKey:key) } label:{Image(systemName:"minus.circle")}.buttonStyle(.plain) }
                    } }
                }
            }
            HStack { Button("Remove All") { metadata=[:];metadataRemoveAll=true };Spacer();if !isGIFMetadata { Button("Add Field") { newMetadataKey=category == .image ? "{XMP-dc}.Title":category == .pdf ? "Title":"file:title";newMetadataValue="";addingMetadataField=true }.popover(isPresented:$addingMetadataField) {
                VStack(alignment:.leading,spacing:12) { Text("Add Metadata Field").font(.headline);TextField("Field",text:$newMetadataKey);TextField("Value",text:$newMetadataValue);Button("Add") { metadata[newMetadataKey]=newMetadataValue;addingMetadataField=false }.disabled(newMetadataKey.isEmpty || (usesImageMetadata && !ImageMetadataEngine.isWritable(newMetadataKey))) }.padding(18).frame(width:300)
            } } }
            if isGIFMetadata { Text("Frame timing and repeat count are preserved.").font(.caption).foregroundStyle(.secondary) }
            if category == .image { Text("Encoded pixels, color profile and display orientation are preserved.").font(.caption).foregroundStyle(.secondary) }
            if category == .image { Button("Read QR Codes") { if let input { qrResults=(try? ImageEngine.qrCodes(input)) ?? [] } };ForEach(qrResults,id:\.self) { Text($0).textSelection(.enabled) } }
        }
    }
    var visibleMetadata:[String] { metadata.keys.sorted().filter{metadataSearch.isEmpty || $0.localizedCaseInsensitiveContains(metadataSearch) || (metadata[$0] ?? "").localizedCaseInsensitiveContains(metadataSearch)} }
    var metadataGroups:[String] { Array(Set(visibleMetadata.map(metadataGroup))).sorted() }
    func metadataGroup(_ key:String)->String { if let separator=key.firstIndex(of:category == .image ? ".":":") { return String(key[..<separator]) };return category == .pdf ? "Document":"File" }
    var imagePreview:some View {
        ImageEditPreview(image:showOriginal ? baseImage.map{NSImage(cgImage:$0,size:.zero)}:image,imageSize:showOriginal ? imageSize:(previewSize ?? imageSize),crop:tool == .cropImage ? options.crop:nil,redactions:options.redactions,annotations:options.imageAnnotations,redactionColor:Color(cgColor:ImageEngine.color(options.backgroundColor).copy(alpha:1)!),zoom:previewZoom,onStroke:tool == .annotateImage && !selectingAnnotations ? addAnnotation:nil,onMoveAnnotation:{i,r,points in options.imageAnnotations[i].rect=r;options.imageAnnotations[i].points=points},onMoveRedaction:{i,r in options.redactions[i]=r},onMoveCrop:{r in setCrop(r)},cropEnabled:tool == .cropImage,cropAspectRatio:cropAspectRatio,onCropInteraction:cropInteraction,annotationStyle:tool == .annotateImage ? annotationStyle:nil,annotationSource:baseImage,selectAnnotations:selectingAnnotations) { rect in
            switch tool {
            case .cropImage: setCrop(rect)
            case .redactImage: options.redactions.append(rect)
            case .annotateImage: break
            default: break
            }
        }.frame(height:420).background(Color.black.opacity(0.94)).cornerRadius(20)
    }
    var mediaPreview:some View {
        VStack(spacing:10) {
            if let player {
                ZStack {
                    PlayerView(player:player,onTime:updatePlayhead,onPlayback:{isPlaying=$0},hiddenControls:true)
                        .id(ObjectIdentifier(player)).frame(height:audio && tool != .audioToVideo ? 0:330)
                    if (tool == .cropVideo || tool == .redactVideo) && !processedPlayback { ImageEditPreview(image:nil,imageSize:imageSize,crop:tool == .cropVideo ? options.crop:nil,redactions:options.redactions,annotations:[],redactionColor:Color(cgColor:ImageEngine.color(options.backgroundColor).copy(alpha:1)!),activeRedactions:tool == .redactVideo ? activeVideoRegions:nil,onMoveRedaction:{i,r in options.redactions[i]=r;if options.videoRedactions.indices.contains(i) { options.videoRedactions[i].rect=r }},onMoveCrop:setCrop,cropEnabled:tool == .cropVideo,cropAspectRatio:cropAspectRatio,onCropInteraction:cropInteraction) { rect in if tool == .cropVideo { setCrop(rect) } else { options.redactions.append(rect);options.videoRedactions.append(VideoRedaction(rect:rect,start:options.start,end:options.end)) } }.frame(height:330) }
                }.cornerRadius(10)
            }
            if tool == .audioToVideo {
                if previewBusy { ProgressView("Preparing visualization…") }
                if visualizationReady { Text("Preview uses the first 12 seconds. Export includes the full recording.").font(.caption).foregroundStyle(.secondary) }
            }
            if !(tool == .splitVideo && processedPlayback) { mediaTimeline }
            if audio && tool != .audioToVideo {
                if waveform.isEmpty { ProgressView("Reading waveform…").frame(height:80) }
                else if tool == .audioChannels,!leftWaveform.isEmpty {
                    channelWaveform("L",peaks:leftWaveform,gain:options.leftVolume)
                    channelWaveform("R",peaks:rightWaveform,gain:options.rightVolume)
                }
                else { AudioWaveformView(peaks:processedWaveform ?? waveform,duration:duration,current:playhead,selection:tool == .trimAudio ? TimeRange(start:options.start,end:options.end ?? duration):nil,redactions:options.audioRedactions,rangeEnabled:tool == .trimAudio || tool == .redactAudio,onSeek:seek,onRange:{start,end in options.start=start;options.end=end;if tool == .redactAudio { options.audioRedactions.append(TimeRange(start:start,end:end)) }}).frame(height:90) }
            }
            if duration>0 { Slider(value:Binding(get:{min(playhead,playbackDuration)},set:seek),in:0...max(0.001,playbackDuration)).tint(.nashi) }
            HStack { Button { seek(max(0,playhead-1/frameRate)) } label:{Image(systemName:"backward.end")}.accessibilityLabel("Previous frame").help("Previous frame");Button(action:togglePlayback) { Image(systemName:isPlaying ? "pause.fill":"play.fill") }.accessibilityLabel(isPlaying ? "Pause":"Play").help("Play / Pause (Space)");Button { seek(min(playbackDuration,playhead+1/frameRate)) } label:{Image(systemName:"forward.end")}.accessibilityLabel("Next frame").help("Next frame");Spacer();Text("\(timeLabel(playhead)) / \(timeLabel(playbackDuration))").font(.caption.monospacedDigit()) }
            if (audio && tool != .audioToVideo && tool != .trimAudio) || tool == .redactVideo { HStack { Button("Original") { playOriginal() };Button(previewBusy ? "Preparing…":"Preview Processed") { processedPreview() }.disabled(previewBusy || (tool == .redactVideo || tool == .redactAudio) && !canApply) } }
            if tool == .redactVideo,processedPlayback { Text("Processed preview. Choose Original to edit areas.").font(.caption).foregroundStyle(.secondary) }
            if tool == .trimAudio || tool == .trimVideo { Button("Play Selected Range",action:playSelectedRange).help("Play selected range (⇧Space)") }
            LText(mediaShortcutHint).font(.system(size:10)).foregroundStyle(.secondary)
                .fixedSize(horizontal:false,vertical:true).frame(maxWidth:.infinity,alignment:.leading)
        }
    }
    var playbackDuration:Double { if processedPlayback,let processedDuration { return processedDuration };return tool == .audioToVideo && visualizationReady ? min(duration,12):duration }
    var activeVideoRegions:Set<Int> { options.redactionMode == "blur" ? []:Set(options.videoRedactions.enumerated().filter{playhead >= $0.element.start && playhead < ($0.element.end ?? duration)}.map{$0.offset}) }
    var compressionResultText:String {
        func bytes(_ urls:[URL])->Int64 { urls.reduce(0) { $0+((try? FileManager.default.attributesOfItem(atPath:$1.path)[.size] as? NSNumber)?.int64Value ?? 0) } }
        let before=bytes(inputs),after=bytes(state.outputs)
        if before==after { return "Already optimized. Original quality retained." }
        let size=ByteCountFormatter.string(fromByteCount:after,countStyle:.file)
        return before>after && before>0 ? "\(size) · \(Int((1-Double(after)/Double(before))*100))% smaller":"Saved size: \(size)"
    }
    @ViewBuilder var mediaTimeline:some View {
        if duration>0 {
            if tool == .trimVideo || tool == .trimAudio {
                MediaTimeRangeLane(duration:duration,current:playhead,ranges:[TimeRange(start:options.start,end:options.end ?? duration)],label:"Trim",source:playbackURL ?? input,thumbnails:tool == .trimVideo,onSeek:seek,onChange:{_,range in options.start=range.start;options.end=range.end})
            } else if tool == .redactAudio {
                Text("Drag a range to move it; drag its edges to resize. Drag empty space to add.").font(.caption).foregroundStyle(.secondary)
                MediaTimeRangeLane(duration:duration,current:playhead,ranges:options.audioRedactions,label:options.audioMode == "mute" ? "Mute":"Bleep",onSeek:seek,onChange:{i,r in options.audioRedactions[i]=r},onAdd:{options.audioRedactions.append($0)})
            } else if tool == .redactVideo {
                ForEach(Array(options.videoRedactions.enumerated()),id:\.offset) { i,r in
                    HStack { LText("Area \(i+1)").font(.caption).frame(width:45);MediaTimeRangeLane(duration:duration,current:playhead,ranges:[TimeRange(start:r.start,end:r.end ?? duration)],label:"",onSeek:seek,onChange:{_,range in options.videoRedactions[i].start=range.start;options.videoRedactions[i].end=range.end}) }
                }
            } else if let source=playbackURL ?? input,tool == .splitVideo || tool == .videoSnapshots {
                VideoPointTimeline(source:source,duration:duration,current:playhead,points:tool == .splitVideo ? options.splitPoints:[],onSeek:seek,onChange:{i,time in options.equalClips=nil;let lower=i>0 ? options.splitPoints[i-1]+0.05:0.05;let upper=i+1<options.splitPoints.count ? options.splitPoints[i+1]-0.05:duration-0.05;options.splitPoints[i]=min(max(time,lower),max(lower,upper))})
            }
        }
    }
    func channelWaveform(_ title:String,peaks:[Float],gain:Double)->some View {
        HStack { LText(title).font(.caption.bold()).frame(width:15);AudioWaveformView(peaks:peaks.map{min(1,$0*Float(gain))},duration:duration,current:playhead,selection:nil,redactions:[],rangeEnabled:false,onSeek:seek,onRange:{_,_ in}).frame(height:50) }
    }
    func load() async {
        guard let input else { return }
        let generation=editorGeneration,token=previewToken
        isLoading=true;readyToApply=false
        defer { if generation==editorGeneration,!token.isCancelled { isLoading=false } }
        do {
            try checkGeneration(generation,token:token)
            if category == .image {
                let cg=try await Task.detached { try ImageEngine.load(input) }.value
                try checkGeneration(generation,token:token)
                baseImage=cg;image=NSImage(cgImage:cg,size:.zero);imageSize=CGSize(width:cg.width,height:cg.height);width=cg.width;height=cg.height
                if tool == .compress { setCompressionDimensions(compressionSize) }
            }
            if category == .audio || category == .video {
                let info=try await Task.detached { try MediaEngine.inspect(input,token:token) }.value
                try checkGeneration(generation,token:token)
                duration=info.duration;options.end=duration
                if let v=info.video { imageSize=CGSize(width:v.width ?? 100,height:v.height ?? 100);width=v.width ?? 100;height=v.height ?? 100;frameRate=max(1,v.frameRate ?? 30) }
                if category == .video { let generator=AVAssetImageGenerator(asset:AVURLAsset(url:input));generator.appliesPreferredTrackTransform=true;if let cg=try? generator.copyCGImage(at:.zero,actualTime:nil) { image=NSImage(cgImage:cg,size:.zero) } }
                if audio || video {
                    let prepared=try await withTaskCancellationHandler(operation:{try await MediaPreviewEngine.prepare(input,token:token)},onCancel:{token.cancel()})
                    guard generation==editorGeneration,!token.isCancelled,!Task.isCancelled else { prepared.removeTemporaryFiles();return }
                    player?.pause();if let old=mediaPreviewFolder { try? FileManager.default.removeItem(at:old) };mediaPreviewFolder=prepared.ownedTemporaryDirectory;playbackURL=prepared.playbackURL;playerReachedEnd=false;player=AVPlayer(url:prepared.playbackURL)
                    if category == .audio,tool != .audioToVideo {
                        let peaks=try await Task.detached { try MediaPreviewEngine.waveformPeaks(input,token:token) }.value
                        try checkGeneration(generation,token:token);waveform=peaks
                        if tool == .audioChannels {
                            let left=try await Task.detached { try MediaPreviewEngine.waveformPeaks(input,channel:0,token:token) }.value
                            try checkGeneration(generation,token:token);leftWaveform=left
                            let right=(info.audio?.channels ?? 1)>1 ? try await Task.detached { try MediaPreviewEngine.waveformPeaks(input,channel:1,token:token) }.value:left
                            try checkGeneration(generation,token:token);rightWaveform=right
                        }
                    }
                }
            }
            if tool == .removeMetadata {
                let loaded:[String:String]
                if usesImageMetadata { loaded=try await Task.detached { try ImageEngine.readMetadata(input,token:token) }.value }
                else if category == .pdf { let pdf=try PDFEngine.open(input);var values:[String:String]=[:];for (key,value) in pdf.documentAttributes ?? [:] { values[String(describing:key)]=String(describing:value) };loaded=values }
                else { loaded=try await Task.detached { try MediaEngine.readMetadata(input,token:token) }.value }
                try checkGeneration(generation,token:token);metadata=loaded;originalMetadata=loaded
            }
            if tool == .organizePDF { pdfEditor.reset(document:try PDFEngine.open(input)) }
            if tool == .frameImage { options.imageFrame = .init() }
            if tool == .createCollage { options.collage = .init() }
            if tool == .audioToVideo { width=1920;height=1080;options.backgroundColor="#000000" }
            if tool == .redactVideo || tool == .redactImage { options.backgroundColor="#000000";options.redactionMode="black" }
            try checkGeneration(generation,token:token);readyToApply=true
        } catch {
            if generation==editorGeneration,!token.isCancelled,!Task.isCancelled { previewError=error.localizedDescription }
        }
    }
    func reset() {
        disposeMedia();previewToken=CancellationToken();visualizationToken=CancellationToken()
        isLoading=true;readyToApply=false;inputs=originalInputs;options=OperationOptions()
        pdfEditor.reset();selectedSplitClip=nil;aspect="Free";cropInteracting=false;equalClips=2
        width=0;height=0;image=nil;baseImage=nil;imageSize=CGSize(width:100,height:100);previewSize=nil;previewZoom=1;showOriginal=false
        duration=0;frameRate=30;playhead=0;playerReachedEnd=false;playingRange=false;processedPlayback=false;processedDuration=nil
        waveform=[];leftWaveform=[];rightWaveform=[];processedWaveform=nil;playbackURL=nil;visualizationReady=false;previewBusy=false;previewError=nil
        metadata=[:];originalMetadata=[:];metadataSearch="";metadataRemoveAll=false;addingMetadataField=false;newMetadataKey="{XMP-dc}.Title";newMetadataValue="";qrResults=[]
        annotationKind = .arrow;selectingAnnotations=false;annotationText="Text";annotationColor = .red;annotationStroke=3;annotationFont=24;annotationFontFamily="system"
        targetEnabled=false;targetMode="MB";targetValue=1;preset="Balanced";compressionSize="original"
        if tool == .compress {
            let image=category == .image
            preset=(image ? FormatPreferences.shared.imageCompressionStrength:FormatPreferences.shared.videoCompressionStrength)=="strong" ? "Strong":"Balanced"
            compressionSize=image ? FormatPreferences.shared.imageCompressionSize:"original"
        }
        loadRevision=UUID()
    }
    func checkGeneration(_ generation:UUID,token:CancellationToken) throws {
        try Task.checkCancellation();try token.check()
        if generation != editorGeneration { throw CancellationError() }
    }
    func apply(joinedSplit:Bool=false) {
        guard canApply else { return }
        if tool == .organizePDF { saveOrganizedPDF();return }
        var applied=options
        if tool == .splitVideo { applied.joinSplitClips=joinedSplit }
        if width>0 { applied.width=width };if height>0 { applied.height=height }
        if [.cropImage,.cropVideo].contains(tool),var crop=applied.crop { crop.width=Double(width);crop.height=Double(height);applied.crop=crop }
        if tool == .cropImage { applied.width=nil;applied.height=nil }
        if tool == .compress {
            applied.quality=preset=="Balanced" ? 0.8:0.45
            if targetEnabled {
                guard targetValue.isFinite,targetValue>0 else { previewError="Enter a positive, finite target size.";return }
                if targetMode=="percent" {
                    guard targetValue<100 else { previewError="Enter a reduction greater than 0% and less than 100%.";return }
                    applied.percentage=targetValue
                } else {
                    let bytes=targetValue*(targetMode=="MB" ? 1_000_000:1_000)
                    guard bytes.isFinite,bytes>=1,bytes<Double(Int64.max) else { previewError="Enter a target of at least one byte and less than \(Int64.max) bytes.";return }
                    applied.targetBytes=Int64(bytes)
                }
            }
        }
        if tool == .redactImage { var redact=options.imageRedaction ?? ImageRedactionOptions();redact.style=options.redactionMode=="black" ? .solid:options.redactionMode=="pixelate" ? .pixelate:.blur;redact.color=options.backgroundColor;applied.imageRedaction=redact }
        if tool == .removeMetadata {
            applied.metadata=metadata
            if !usesImageMetadata && (category == .audio || category == .video) { for key in originalMetadata.keys where metadata[key]==nil { applied.metadata[key]="" } }
            applied.metadataRemoveAll=metadataRemoveAll
            if usesImageMetadata { var edit=ImageMetadataOptions();edit.removeAll=metadataRemoveAll;edit.updates=metadata.filter{originalMetadata[$0.key] != $0.value};edit.removals=metadataRemoveAll ? []:originalMetadata.keys.filter{metadata[$0]==nil};applied.imageMetadata=edit }
        }
        state.execute(.tool(tool),options:applied,inputs:inputs)
    }
    func saveOrganizedPDF() {
        guard canApply,!pdfEditor.pages.isEmpty else { return }
        var settings=options;settings.pdfPageOrder=pdfEditor.sourceIndexes;settings.pdfPageRotations=pdfEditor.rotations
        state.execute(.tool(.organizePDF),options:settings,inputs:inputs)
    }
    func detectRedactions(faces:Bool) {
        guard let input,readyToApply,!isLoading else { return }
        let generation=editorGeneration,token=previewToken
        Task { do {
            let regions=try await Task.detached { try ImageEngine.recognizedRegions(input,faces:faces,text:!faces) }.value
            try checkGeneration(generation,token:token);options.redactions += regions.map(\.rect)
        } catch { if generation==editorGeneration,!token.isCancelled { previewError=error.localizedDescription } } }
    }
    var previewKey:Data { let encoder=JSONEncoder();encoder.outputFormatting = .sortedKeys;var data=(try? encoder.encode(options)) ?? Data();data.append(contentsOf:inputs.map(\.path).joined(separator:"\0").utf8);return data }
    func renderPreview() async {
        guard let baseImage,visual,tool != .cropImage else { return }
        let generation=editorGeneration,token=previewToken
        do {
            try await Task.sleep(nanoseconds:120_000_000);try Task.checkCancellation()
            var o=options
            if tool == .redactImage { var r=o.imageRedaction ?? .init();r.style=o.redactionMode=="black" ? .solid:o.redactionMode=="pixelate" ? .pixelate:.blur;r.color=o.backgroundColor;o.imageRedaction=r }
            let kind=tool,sources=inputs
            let cg=try await Task.detached { if kind == .createCollage { return try ImageEngine.collageImage(sources.map{try ImageEngine.load($0)},settings:o.collage ?? .init()) };return try ImageEngine.preview(baseImage,tool:kind,options:o) }.value
            try checkGeneration(generation,token:token);image=NSImage(cgImage:cg,size:.zero);previewSize=CGSize(width:cg.width,height:cg.height)
        } catch { /* A later edit cancels an obsolete preview; Apply reports export errors. */ }
    }
    var annotationStyle:ImageAnnotation {
        var a=ImageAnnotation(kind:annotationKind,rect:.init(x:0,y:0,width:0,height:0));a.text=annotationText;a.color=hex(annotationColor);a.number=options.imageAnnotations.count+1;a.strokeWidth=annotationStroke;a.fontSize=annotationFont;a.fontFamily=annotationFontFamily
        return a
    }
    func addAnnotation(_ rect:EditRect,_ points:[ImagePoint]) {
        guard var a=AnnotationDrawing.draft(style:annotationStyle,points:points) else { return }
        if annotationKind == .image { let panel=NSOpenPanel();guard panel.runModal() == .OK else { return };a.imageURL=panel.url }
        options.imageAnnotations.append(a)
    }
    func setCrop(_ rect:EditRect) {
        var r=rect
        // Whole pixels, rounded down, except that floating-point noise just under an
        // integer (a locked ratio's 0.9999999999999999) keeps that integer.
        func pixels(_ v:Double)->Double { let n=v.rounded();return abs(v-n)<1e-6 ? n:v.rounded(.down) }
        r.x=pixels(r.x);r.y=pixels(r.y)
        r.width=max(1,pixels(r.width));r.height=max(1,pixels(r.height))
        options.crop=r;width=Int(r.width);height=Int(r.height)
    }
    func previewChannel(_ channel:Int) {
        guard let input,readyToApply,!isLoading else { return };let settings=options,token=previewToken,generation=editorGeneration
        Task { do {
            let url=try await Task.detached { try MediaEngine.previewChannel(input,channel:channel,options:settings,token:token) }.value
            guard generation==editorGeneration,!token.isCancelled else { try? FileManager.default.removeItem(at:url.deletingLastPathComponent());return }
            player?.pause();if let old=channelPreviewURL { try? FileManager.default.removeItem(at:old.deletingLastPathComponent()) };channelPreviewURL=url;playerReachedEnd=false;player=AVPlayer(url:url);processedPlayback=true;player?.play()
        } catch { if generation==editorGeneration,!token.isCancelled { previewError=error.localizedDescription } } }
    }
    var mediaShortcutHint:String {
        let fine=video ? "⇧←/→ 1 frame":"⇧←/→ 0.1s"
        let extra=(tool == .trimAudio || tool == .trimVideo) ? " · I/O set range · ⇧Space play range":tool == .splitVideo ? " · M add split":tool == .videoSnapshots ? " · M add frame":""
        return "Space play/pause · ←/→ 1s · \(fine)\(extra)"
    }
    var canAddSplitPoint:Bool { !processedPlayback && playhead>=0.05 && duration-playhead>=0.05 && !options.splitPoints.contains{abs($0-playhead)<0.05} }
    func addSplitPoint() {
        guard canAddSplitPoint else { return }
        options.equalClips=nil;options.splitPoints.append(playhead);options.splitPoints.sort()
    }
    func handleShortcut(_ command:EditorKeyboardCommand)->Bool {
        guard !isLoading,readyToApply,!state.working else { return false }
        if command == .save { guard canApply else { return false };apply();return true }
        if command == .confirmCrop { guard isCropTool,canApply else { return false };apply();return true }
        guard (audio || video),player != nil,!previewBusy else { return false }
        switch command {
        case .togglePlayback: togglePlayback()
        case .nudge(let direction,let fine):
            seek(EditorKeyboardCommand.seekTarget(current:playhead,duration:playbackDuration,direction:direction,fine:fine,video:video,frameRate:frameRate))
        case .beginning: seek(0)
        case .end: seek(playbackDuration)
        case .playRange:
            guard tool == .trimAudio || tool == .trimVideo else { return false };playSelectedRange()
        case .markIn:
            guard tool == .trimAudio || tool == .trimVideo else { return false }
            options.start=min(playhead,max(0,duration-0.01))
            if (options.end ?? duration)<=options.start { options.end=duration }
        case .markOut:
            guard tool == .trimAudio || tool == .trimVideo else { return false }
            options.end=min(duration,max(0.01,playhead))
            if options.start>=(options.end ?? duration) { options.start=0 }
        case .addMarker:
            guard !processedPlayback else { return false }
            if tool == .splitVideo { addSplitPoint() }
            else if tool == .videoSnapshots { options.snapshotTimes.append(playhead) }
            else { return false }
        case .save,.confirmCrop: break
        }
        return true
    }
    func playSelectedRange() {
        let end=options.end ?? duration
        guard options.start.isFinite,end.isFinite,options.start>=0,end>options.start,end<=duration else {
            previewError="Choose a valid start and end within the file’s duration.";return
        }
        seek(options.start);playingRange=true;player?.play()
    }
    func updatePlayhead(_ time:Double) {
        guard time.isFinite,!seeking else { return }
        if playingRange,time>=(options.end ?? duration) {
            player?.pause();seek(options.end ?? duration)
        } else { playhead=time }
    }
    func seek(_ value:Double) {
        guard value.isFinite,value>=0,playbackDuration.isFinite else { previewError="Enter a valid time within the file’s duration.";return }
        let time=min(value,max(0,playbackDuration))
        playingRange=false;playerReachedEnd=false;playhead=time
        let revision=UUID();seekRevision=revision
        guard let player else { seeking=false;return }
        seeking=true
        player.seek(to:CMTime(seconds:time,preferredTimescale:60_000),toleranceBefore:.zero,toleranceAfter:.zero) { finished in
            DispatchQueue.main.async {
                guard seekRevision==revision,self.player === player else { return }
                seeking=false
                if finished { playhead=time }
            }
        }
    }
    func togglePlayback() {
        guard let player else { return }
        var ended=playerReachedEnd
        if let end=player.currentItem?.duration.seconds,end.isFinite,player.currentTime().seconds>=end-0.001 { ended=true }
        if player.rate != 0 && !ended { player.pause();return }
        if ended { seek(0) }
        if tool == .changeVideoSpeed { player.currentItem?.audioTimePitchAlgorithm = .spectral;player.rate=Float(options.speed) } else { player.play() }
    }
    func playOriginal(play:Bool=true) { processedPlayback=false;processedWaveform=nil;processedDuration=nil;if let url=playbackURL ?? input { let time=playhead;player?.pause();player=AVPlayer(url:url);seek(time);if play { if time>=duration-0.001 { seek(0) };player?.play() } } }
    func processedPreview() {
        guard let input,readyToApply,!isLoading else { return };var settings=options;let kind=tool,token=previewToken,generation=editorGeneration
        if kind == .splitVideo { settings.joinSplitClips=true }
        previewBusy=true
        Task { do {
            let preview=try await MediaPreviewEngine.processedPreview(kind,input:input,options:settings,token:token)
            guard generation==editorGeneration,!token.isCancelled else { preview.removeTemporaryFiles();return }
            processedDuration=(try? MediaEngine.inspect(preview.playbackURL).duration)
            player?.pause();if let old=channelPreviewURL { try? FileManager.default.removeItem(at:old.deletingLastPathComponent()) };channelPreviewURL=preview.playbackURL;playerReachedEnd=false;player=AVPlayer(url:preview.playbackURL);processedPlayback=true;player?.play()
            if kind == .normalizeAudio { let url=preview.playbackURL;let peaks=try await Task.detached { try MediaPreviewEngine.waveformPeaks(url,token:token) }.value;try checkGeneration(generation,token:token);processedWaveform=peaks }
        } catch { if generation==editorGeneration,!token.isCancelled { previewError=error.localizedDescription } }
            if generation==editorGeneration,!token.isCancelled { previewBusy=false }
        }
    }
    var visualizationKey:String { tool == .audioToVideo ? "\(input?.path ?? ""):\(options.visualizationStyle):\(options.visualizationImage?.path ?? ""):\(options.backgroundColor):\(width):\(height)":"" }
    func renderVisualization() async {
        guard tool == .audioToVideo,let input,width>0,height>0 else { return }
        if options.visualizationStyle == "image",options.visualizationImage == nil { return }
        visualizationToken.cancel();let token=CancellationToken(),generation=editorGeneration;visualizationToken=token
        var settings=options;settings.width=width;settings.height=height
        do {
            try await Task.sleep(nanoseconds:150_000_000);try Task.checkCancellation();previewBusy=true
            let preview=try await withTaskCancellationHandler(operation:{ try await MediaPreviewEngine.visualizationPreview(input,options:settings,token:token) },onCancel:{token.cancel()})
            if Task.isCancelled || token.isCancelled || generation != editorGeneration { preview.removeTemporaryFiles();return }
            player?.pause();if let old=channelPreviewURL { try? FileManager.default.removeItem(at:old.deletingLastPathComponent()) };channelPreviewURL=preview.playbackURL;playerReachedEnd=false;player=AVPlayer(url:preview.playbackURL);playhead=0;visualizationReady=true;previewBusy=false
        } catch { if generation==editorGeneration,!Task.isCancelled,!token.isCancelled { previewBusy=false;previewError=error.localizedDescription } }
    }
    func disposeMedia() {
        editorGeneration=UUID();readyToApply=false;seekRevision=UUID();seeking=false;isPlaying=false
        previewToken.cancel();visualizationToken.cancel();player?.pause();player?.replaceCurrentItem(with:nil);player=nil
        if let channelPreviewURL { try? FileManager.default.removeItem(at:channelPreviewURL.deletingLastPathComponent());self.channelPreviewURL=nil }
        if let mediaPreviewFolder { try? FileManager.default.removeItem(at:mediaPreviewFolder);self.mediaPreviewFolder=nil }
    }
    func frame<T>(_ key:WritableKeyPath<ImageFrameOptions,T>)->Binding<T> { Binding(get:{(options.imageFrame ?? .init())[keyPath:key]},set:{var value=options.imageFrame ?? .init();value[keyPath:key]=$0;options.imageFrame=value}) }
    func collage<T>(_ key:WritableKeyPath<CollageOptions,T>)->Binding<T> { Binding(get:{(options.collage ?? .init())[keyPath:key]},set:{var value=options.collage ?? .init();value[keyPath:key]=$0;options.collage=value}) }
    func relative(_ value:Binding<Double>)->Binding<Double> { Binding(get:{value.wrappedValue-1},set:{value.wrappedValue=$0+1}) }
    func number(_ label:String,value:Binding<Int>)->some View { HStack { Text(label);TextField("",value:value,format:.number).textFieldStyle(.roundedBorder).frame(minWidth:60) } }
    func slider(_ title:String,value:Binding<Double>,range:ClosedRange<Double>,unit:String="",reset:Double?=nil)->some View { HStack { LText(title).font(.callout).frame(width:105,alignment:.leading);Slider(value:value,in:range).tint(.nashi);Text("\(value.wrappedValue,specifier:"%.2f") \(unit)").font(.caption.monospacedDigit()).frame(width:70,alignment:.trailing);Button { value.wrappedValue=reset ?? ["Playback speed":1,"Target loudness":-16,"Loudness range":11,"True peak limit":-1.5,"Left volume":1,"Right volume":1,"Padding":160,"Corners":24,"Shadow":24,"Stroke width":3,"Text size":24,"Silence threshold":-50][title] ?? 0 } label:{Image(systemName:"arrow.counterclockwise").font(.caption)}.buttonStyle(.plain) } }
    func colorBinding(_ string:Binding<String>,preservingOpacity:Bool=false)->Binding<Color> {
        Binding(get:{Color(cgColor:ImageEngine.color(string.wrappedValue))},set:{
            string.wrappedValue=preservingOpacity ? ImageColorCodec.backgroundHex(NSColor($0)):hex($0)
        })
    }
    func hex(_ color:Color)->String { guard let rgb=NSColor(color).usingColorSpace(.sRGB) else{return "#FFFFFF"};return String(format:"#%02X%02X%02X",Int(rgb.redComponent*255),Int(rgb.greenComponent*255),Int(rgb.blueComponent*255)) }
    func timeLabel(_ time:Double)->String {
        guard time.isFinite,time>=0,time<Double(Int.max) else { return "—" }
        // Round to the displayed millisecond first so 59.9996 carries into the minute.
        let rounded=(time*1000).rounded()/1000
        guard rounded<Double(Int.max) else { return "—" }
        return String(format:"%02ld:%06.3f",Int(rounded)/60,rounded.truncatingRemainder(dividingBy:60))
    }
}

struct WindowCloseButton:NSViewRepresentable {
    final class Coordinator:NSObject { @objc func close(_ sender:NSButton) { sender.window?.close() } }
    func makeCoordinator()->Coordinator { Coordinator() }
    func makeNSView(context:Context)->NSButton { let button=NSButton(image:NSImage(systemSymbolName:"xmark.circle.fill",accessibilityDescription:"Close")!,target:context.coordinator,action:#selector(Coordinator.close(_:)));button.isBordered=false;button.setAccessibilityLabel("Close");return button }
    func updateNSView(_ nsView:NSButton,context:Context) { }
}
struct WindowDragRegion:NSViewRepresentable {
    final class DragView:NSView { override func mouseDown(with event:NSEvent) { window?.performDrag(with:event) } }
    func makeNSView(context:Context)->DragView { let view=DragView();view.setAccessibilityElement(false);return view }
    func updateNSView(_ view:DragView,context:Context) { }
}
struct WindowIdentityReader:NSViewRepresentable {
    var onWindow:(ObjectIdentifier)->Void
    final class Reader:NSView {
        var onWindow:((ObjectIdentifier)->Void)?
        override func viewDidMoveToWindow() { super.viewDidMoveToWindow();if let window { let id=ObjectIdentifier(window);DispatchQueue.main.async { [weak self] in self?.onWindow?(id) } } }
    }
    func makeNSView(context:Context)->Reader { let view=Reader();view.onWindow=onWindow;return view }
    func updateNSView(_ nsView:Reader,context:Context) { nsView.onWindow=onWindow }
}

struct PlayerView:NSViewRepresentable {
    let player:AVPlayer
    var onTime:(Double)->Void = {_ in}
    var onPlayback:(Bool)->Void = {_ in}
    var hiddenControls=false
    final class Coordinator {
        var player:AVPlayer?
        var token:Any?
        var rateObserver:NSKeyValueObservation?
        var onPlayback:(Bool)->Void = {_ in}
        var onTime:(Double)->Void = {_ in}
        func observe(_ player:AVPlayer,onTime:@escaping(Double)->Void,onPlayback:@escaping(Bool)->Void) {
            self.onPlayback=onPlayback
            if self.player !== player {
                stopObserving();self.player=player
                token=player.addPeriodicTimeObserver(forInterval:CMTime(seconds:1/30,preferredTimescale:600),queue:.main) { [weak self,weak player] time in guard let player,self?.player === player else { return };self?.onTime(time.seconds) }
                rateObserver=player.observe(\.rate,options:[.initial,.new]) { [weak self] player,_ in
                    DispatchQueue.main.async { [weak self] in guard self?.player === player else { return };self?.onPlayback(player.rate != 0) }
                }
            }
            self.onTime=onTime;self.onPlayback=onPlayback
        }
        func stopObserving() {
            if let token { player?.removeTimeObserver(token) }
            rateObserver=nil;token=nil;player=nil;onTime={_ in};onPlayback={_ in}
        }
        deinit { stopObserving() }
    }
    func makeCoordinator()->Coordinator { Coordinator() }
    func makeNSView(context:Context)->AVPlayerView {
        let view=AVPlayerView();view.controlsStyle=hiddenControls ? .none:.inline
        view.player=player;context.coordinator.observe(player,onTime:onTime,onPlayback:onPlayback);return view
    }
    func updateNSView(_ view:AVPlayerView,context:Context) {
        if view.player !== player { view.player=player }
        let style:AVPlayerViewControlsStyle=hiddenControls ? .none:.inline
        if view.controlsStyle != style { view.controlsStyle=style }
        context.coordinator.observe(player,onTime:onTime,onPlayback:onPlayback)
    }
    static func dismantleNSView(_ view:AVPlayerView,coordinator:Coordinator) {
        coordinator.stopObserving();view.player=nil
    }
}

struct AudioWaveformView:View {
    var peaks:[Float];var duration:Double;var current:Double;var selection:TimeRange?;var redactions:[TimeRange];var rangeEnabled:Bool
    var onSeek:(Double)->Void;var onRange:(Double,Double)->Void
    @State private var dragging:TimeRange?
    var body:some View {
        GeometryReader { geometry in
            Canvas { context,size in
                let w=size.width/CGFloat(max(1,peaks.count))
                for (i,p) in peaks.enumerated() { let h=max(1,CGFloat(sqrt(abs(p)))*(size.height-12));let rect=CGRect(x:CGFloat(i)*w,y:(size.height-h)/2,width:max(1,w*0.8),height:h);context.fill(Path(roundedRect:rect,cornerRadius:1),with:.color(CGFloat(i)*w<=size.width*current/max(0.01,duration) ? .nashi:.secondary.opacity(0.5))) }
                for range in redactions+[selection,dragging].compactMap({$0}) { let rect=CGRect(x:size.width*range.start/max(duration,0.01),y:0,width:size.width*(range.end-range.start)/max(duration,0.01),height:size.height);context.fill(Path(rect),with:.color(.nashi.opacity(0.18)));context.stroke(Path(rect),with:.color(.nashi),lineWidth:1) }
                let x=size.width*current/max(duration,0.01);var cursor=Path();cursor.move(to:CGPoint(x:x,y:0));cursor.addLine(to:CGPoint(x:x,y:size.height));context.stroke(cursor,with:.color(.primary),lineWidth:1)
            }.background(Color.primary.opacity(0.035)).cornerRadius(8).contentShape(Rectangle()).gesture(DragGesture(minimumDistance:0).onChanged { value in
                let a=max(0,min(duration,value.startLocation.x/geometry.size.width*duration)),b=max(0,min(duration,value.location.x/geometry.size.width*duration))
                if rangeEnabled { dragging=TimeRange(start:min(a,b),end:max(a,b)) } else { onSeek(b) }
            }.onEnded { value in if let range=dragging,range.end-range.start>0.01 { onRange(range.start,range.end) } else { onSeek(max(0,min(duration,value.location.x/geometry.size.width*duration))) };dragging=nil })
        }
    }
}
