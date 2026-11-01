import AppKit
import QuickLookUI
import AVKit
import NashiCore

@MainActor final class FilePreview:NSObject {
    private var window:NSWindow?;private var urls:[URL]=[];private var index=0
    private var monitor:Any?;private var player:AVPlayer?;private var preview:QLPreviewView?
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
                // A held Space is one press: its auto-repeat must not close the preview or toggle playback again.
                if event.keyCode==49 {if event.isARepeat {return nil};if let player=self.player {if player.rate==0 {player.play()}else{player.pause()}}else{self.close()};return nil}
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
    @objc private func didClose(){player?.pause();player?.replaceCurrentItem(with:nil);player=nil;preview?.previewItem=nil;preview=nil}
    func close(){window?.close();didClose()}
    private func load() {
        guard let window else{return};didClose();let url=urls[index];window.title=url.lastPathComponent+" · "+L10n.text("Preview")+" · Nashi"
        let root=NSView();window.contentView=root
        let header=NSStackView();header.orientation = .horizontal;header.spacing=8;caption.stringValue="\(url.lastPathComponent) · \(index+1) / \(urls.count)";caption.lineBreakMode = .byTruncatingMiddle;header.addArrangedSubview(caption)
        for (label,selector) in [("←",#selector(previous)),("→",#selector(next)),("Open",#selector(openFile))] {header.addArrangedSubview(NSButton(title:L10n.text(label),target:self,action:selector))}
        let hint=NSTextField(labelWithString:L10n.text("←/→ files · Media: Space play/pause, ←/→ 1s, ⌥←/→ files · Esc closes"));hint.font = .systemFont(ofSize:11);hint.textColor = .secondaryLabelColor
        let content:NSView
        if [.audio,.video].contains(FileCategory.detect(url)) {
            let view=AVPlayerView();let p=AVPlayer(url:url);view.player=p;view.controlsStyle = .floating;player=p;content=view
        } else {let view=QLPreviewView(frame:.zero,style:.normal)!;view.autostarts=false;view.previewItem=url as NSURL;preview=view;content=view}
        for view in [header,content,hint] {view.translatesAutoresizingMaskIntoConstraints=false;root.addSubview(view)}
        NSLayoutConstraint.activate([header.topAnchor.constraint(equalTo:root.topAnchor,constant:16),header.leadingAnchor.constraint(equalTo:root.leadingAnchor,constant:20),header.trailingAnchor.constraint(equalTo:root.trailingAnchor,constant:-20),content.topAnchor.constraint(equalTo:header.bottomAnchor,constant:14),content.leadingAnchor.constraint(equalTo:root.leadingAnchor,constant:20),content.trailingAnchor.constraint(equalTo:root.trailingAnchor,constant:-20),content.bottomAnchor.constraint(equalTo:hint.topAnchor,constant:-14),hint.leadingAnchor.constraint(equalTo:root.leadingAnchor,constant:20),hint.bottomAnchor.constraint(equalTo:root.bottomAnchor,constant:-16)])
    }
}
