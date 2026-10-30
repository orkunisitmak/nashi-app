import AppKit
import Combine
import QuickLookUI
import AVKit
import NashiCore

@MainActor final class FilePreview:NSObject,ObservableObject {
    private var window:NSWindow?;private var urls:[URL]=[];private var index=0
    private var monitor:Any?;private var player:AVPlayer?;private var preview:QLPreviewView?;private var playbackTimer:Timer?
    @Published private(set) var mediaURL:URL?
    @Published private(set) var isPlaying=false
    @Published private(set) var playhead=0.0
    @Published private(set) var duration=0.0
    private let caption=NSTextField(labelWithString:"")
    deinit { if let monitor {NSEvent.removeMonitor(monitor)} }
    func show(_ urls:[URL]) {
        guard !urls.isEmpty else{return};self.urls=urls;index=0
        if window == nil {
            let w=NSWindow(contentRect:NSRect(x:0,y:0,width:800,height:620),styleMask:[.titled,.closable,.resizable,.miniaturizable],backing:.buffered,defer:false);w.backgroundColor=NSColor(NashiPalette.canvas);w.isReleasedWhenClosed=false;w.minSize=NSSize(width:560,height:400);w.center();window=w
            monitor=NSEvent.addLocalMonitorForEvents(matching:.keyDown) { [weak self] event in
                guard let self,event.window===self.window else{return event}
                if event.keyCode==53 {self.close();return nil}
                let modifiers=event.modifierFlags.intersection([.command,.control,.option,.shift]);guard modifiers.isEmpty || modifiers == .option else{return event}
                if event.keyCode==49 {if self.player != nil {self.togglePlayback()}else{self.close()};return nil}
                if [123,124].contains(event.keyCode) {
                    let delta=event.keyCode==123 ? -1:1
                    if let player=self.player,!event.modifierFlags.contains(.option) {let t=player.currentTime().seconds;let duration=player.currentItem?.duration.seconds ?? 0;if t.isFinite,duration.isFinite {player.seek(to:CMTime(seconds:min(max(0,t+Double(delta)),duration),preferredTimescale:600))}}
                    else {self.navigate(delta)};return nil
                };return event
            }
            NotificationCenter.default.addObserver(self,selector:#selector(didClose),name:NSWindow.willCloseNotification,object:w)
        }
        load();window?.makeKeyAndOrderFront(nil)
    }
    private func navigate(_ delta:Int) {index=(index+delta+urls.count)%urls.count;load()}
    @objc private func previous(){navigate(-1)}
    @objc private func next(){navigate(1)}
    @objc private func openFile(){NSWorkspace.shared.open(urls[index])}
    @objc private func didClose(){playbackTimer?.invalidate();playbackTimer=nil;player?.pause();player?.replaceCurrentItem(with:nil);player=nil;preview?.previewItem=nil;preview=nil;mediaURL=nil;isPlaying=false;playhead=0;duration=0}
    func close(){window?.close();didClose()}
    func togglePlayback(){guard let player else{return};if player.rate==0 {player.play()}else{player.pause()};updatePlaybackState(player)}
    func seek(to seconds:Double){
        guard let player,seconds.isFinite,duration.isFinite,duration>0 else{return}
        guard let target=NotchMediaControl.targetTime(current:0,offset:seconds,duration:duration) else{return}
        player.seek(to:CMTime(seconds:target,preferredTimescale:600));playhead=target
    }
    func seek(by seconds:Double){guard let target=NotchMediaControl.targetTime(current:playhead,offset:seconds,duration:duration) else{return};seek(to:target)}
    private func updatePlaybackState(_ player:AVPlayer){
        guard self.player === player else{return}
        let time=player.currentTime().seconds,total=player.currentItem?.duration.seconds ?? 0
        playhead=time.isFinite ? max(0,time):0
        duration=total.isFinite ? max(0,total):0
        isPlaying=player.rate != 0
    }
    private func load() {
        guard let window else{return};didClose();let url=urls[index];window.title=url.lastPathComponent+" · "+L10n.text("Preview")+" · Nashi"
        let root=NSView();window.contentView=root
        let header=NSStackView();header.orientation = .horizontal;header.spacing=8;caption.stringValue="\(url.lastPathComponent) · \(index+1) / \(urls.count)";caption.lineBreakMode = .byTruncatingMiddle;header.addArrangedSubview(caption)
        for (label,selector) in [("←",#selector(previous)),("→",#selector(next)),("Open",#selector(openFile))] {header.addArrangedSubview(NSButton(title:L10n.text(label),target:self,action:selector))}
        let hint=NSTextField(labelWithString:L10n.text("←/→ files · Media: Space play/pause, ←/→ 1s, ⌥←/→ files · Esc closes"));hint.font = .systemFont(ofSize:11);hint.textColor = .secondaryLabelColor
        let content:NSView
        if [.audio,.video].contains(FileCategory.detect(url)) {
            let view=AVPlayerView();let p=AVPlayer(url:url);view.player=p;view.controlsStyle = .floating;player=p;mediaURL=url
            playbackTimer=Timer.scheduledTimer(withTimeInterval:0.25,repeats:true){[weak self,weak p] _ in
                DispatchQueue.main.async { guard let self,let p else{return};self.updatePlaybackState(p) }
            }
            content=view
        } else {let view=QLPreviewView(frame:.zero,style:.normal)!;view.autostarts=false;view.previewItem=url as NSURL;preview=view;content=view}
        for view in [header,content,hint] {view.translatesAutoresizingMaskIntoConstraints=false;root.addSubview(view)}
        NSLayoutConstraint.activate([header.topAnchor.constraint(equalTo:root.topAnchor,constant:16),header.leadingAnchor.constraint(equalTo:root.leadingAnchor,constant:20),header.trailingAnchor.constraint(equalTo:root.trailingAnchor,constant:-20),content.topAnchor.constraint(equalTo:header.bottomAnchor,constant:14),content.leadingAnchor.constraint(equalTo:root.leadingAnchor,constant:20),content.trailingAnchor.constraint(equalTo:root.trailingAnchor,constant:-20),content.bottomAnchor.constraint(equalTo:hint.topAnchor,constant:-14),hint.leadingAnchor.constraint(equalTo:root.leadingAnchor,constant:20),hint.bottomAnchor.constraint(equalTo:root.bottomAnchor,constant:-16)])
    }
}
