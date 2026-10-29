import AppKit
import QuickLookUI
import QuickLookThumbnailing
import UniformTypeIdentifiers

enum FloatShelfEdge:String,CaseIterable { case top,right,bottom,left }
enum FloatShelfViewMode:Int { case icons,list }

@MainActor final class FloatShelfController:NSWindowController,NSWindowDelegate,NSCollectionViewDataSource,NSCollectionViewDelegate,@preconcurrency QLPreviewPanelDataSource,@preconcurrency QLPreviewPanelDelegate,NSMenuItemValidation,NSMenuDelegate {
    var onClose:(()->Void)?
    private(set) var isExpanded=false
    private(set) var viewMode:FloatShelfViewMode = .icons
    private(set) var attachedEdge:FloatShelfEdge?
    var urls:[URL] { items.map(\.url) }
    var selectedURLs:[URL] { selectedIndices.map{items[$0].url} }

    private var items:[ShelfFile]=[]
    private let surface=ShelfDropSurface()
    private let header=ShelfMoveHeader()
    private let collection=ShelfCollectionView()
    private let scroll=NSScrollView()
    private let flow=ShelfColumnLayout()
    private let statusLabel=NSTextField(labelWithString:"")
    private let collapseButton=NSButton()
    private let pinButton=NSButton()
    private let carryGrip=ShelfCarryGrip()
    private let glass=ShelfBackdrop()
    private var hoverTask:Task<Void,Never>?
    private var interacting=false
    private var headerHeight:NSLayoutConstraint!
    private var statusHeight:NSLayoutConstraint!
    private var isAdjustingFrame=false
    private let filePreview=FilePreview()
    private var previewItems:[ShelfPreviewItem]=[]
    private var sharingPicker:NSSharingServicePicker?
    private var typeBuffer=""
    private var lastTypeTime:TimeInterval=0
    private var isClosed=false
    private var languageObserver:NSObjectProtocol?
    private var selectedIndices:[Int] { collection.selectionIndexPaths.map(\.item).filter{items.indices.contains($0)}.sorted() }
    private var actionURLs:[URL] { selectedURLs.isEmpty ? urls:selectedURLs }

    init(urls:[URL]) {
        let panel=ShelfPanel(contentRect:NSRect(origin:.zero,size:NSSize(width:112,height:136)),styleMask:[.borderless,.nonactivatingPanel],backing:.buffered,defer:false)
        super.init(window:panel)
        panel.setAccessibilityRole(.window);panel.setAccessibilitySubrole(.standardWindow)
        panel.title="Float";panel.level = .floating;panel.isFloatingPanel=true;panel.hidesOnDeactivate=false;panel.becomesKeyOnlyIfNeeded=false
        panel.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary];panel.isReleasedWhenClosed=false;panel.isOpaque=false;panel.backgroundColor = .clear;panel.hasShadow=false;panel.minSize=NSSize(width:112,height:136)
        panel.delegate=self;panel.contentView=surface
        buildContent();append(urls)
        let pointer=NSEvent.mouseLocation,visible=(NSScreen.screens.first{$0.frame.contains(pointer)} ?? NSScreen.main)?.visibleFrame ?? .zero
        panel.setFrameOrigin(NSPoint(x:max(visible.minX,min(visible.maxX-112,pointer.x+24)),y:max(visible.minY,min(visible.maxY-136,pointer.y-68))))
        panel.onKey={ [weak self] in self?.handleKey($0) ?? false }
        languageObserver=NotificationCenter.default.addObserver(forName:.nashiLanguageChanged,object:nil,queue:.main){ [weak self] _ in Task { @MainActor in self?.updateLabels() } }
    }
    deinit { if let languageObserver { NotificationCenter.default.removeObserver(languageObserver) } }
    required init?(coder:NSCoder) { fatalError("init(coder:) has not been implemented") }
    override func showWindow(_ sender:Any?) {
        super.showWindow(sender);window?.makeKeyAndOrderFront(sender);window?.makeFirstResponder(isExpanded ? collection:carryGrip)
    }

    func append(_ urls:[URL]) {
        let previousSelection=Set(selectedIndices.map{items[$0].id})
        var present=Set(items.map{$0.url.standardizedFileURL.path})
        for url in urls where url.isFileURL {
            let canonical=url.standardizedFileURL
            if present.insert(canonical.path).inserted { items.append(ShelfFile(url:canonical)) }
        }
        reload(preserving:previousSelection)
        if isExpanded { setExpanded(true,forceLayout:true) }
    }

    func attach(to edge:FloatShelfEdge?) {
        attachedEdge=edge
        (window as? ShelfPanel)?.allowsTucking = edge == .top && !isExpanded
        if let edge,let window,let screen=window.screen ?? NSScreen.main {
            var frame=window.frame;let visible=screen.visibleFrame
            switch edge {
            case .left:frame.origin.x=visible.minX
            case .right:frame.origin.x=visible.maxX-frame.width
            case .top:frame.origin.y=visible.maxY-frame.height
            case .bottom:frame.origin.y=visible.minY
            }
            frame.origin.x=max(visible.minX,min(visible.maxX-frame.width,frame.minX));frame.origin.y=max(visible.minY,min(visible.maxY-frame.height,frame.minY))
            // Leave a small count tab visible while the stack is tucked at the top edge.
            if !isExpanded,edge == .top { frame.origin.y=visible.maxY-frame.height+96 }
            setFrame(frame,animate:true)
        }
        updateLabels()
    }

    private func buildContent() {
        glass.material = .popover;glass.blendingMode = .behindWindow;glass.state = .active
        glass.wantsLayer=true;glass.layer?.cornerRadius=24;glass.layer?.masksToBounds=true
        glass.autoresizingMask=[.width,.height];surface.addSubview(glass)
        surface.onFilesDropped={ [weak self] urls in self?.append(urls) }
        surface.onHover={ [weak self] inside in self?.scheduleHover(inside) }
        header.onMoving={ [weak self] active in self?.interacting=active;self?.hoverTask?.cancel() }
        header.onMoveEnded={ [weak self] in self?.snapAfterMove();self?.scheduleHover(false) }
        let close=button("xmark.circle.fill",help:L10n.text("Close Float without deleting files"),action:#selector(closeShelf))
        close.setAccessibilityIdentifier("float.close")
        pinButton.image=NSImage(systemSymbolName:"ellipsis.circle",accessibilityDescription:L10n.text("Float options"))
        pinButton.bezelStyle = .inline;pinButton.isBordered=false;pinButton.target=self;pinButton.action=#selector(showOptions)
        pinButton.toolTip=L10n.text("Float options");pinButton.setAccessibilityIdentifier("float.options")
        for control in [close,pinButton] { control.translatesAutoresizingMaskIntoConstraints=false;header.addSubview(control) }
        carryGrip.urls={ [weak self] in self?.urls ?? [] }
        carryGrip.contextMenu={ [weak self] in self?.makeContextMenu() }
        carryGrip.onClick={ [weak self] in self?.toggleExpanded() }
        carryGrip.onDragEnded={ [weak self] in self?.interacting=false;self?.refreshAfterDrag();self?.scheduleHover(false) }
        carryGrip.onInteraction={ [weak self] in self?.interacting=$0;self?.hoverTask?.cancel() }
        carryGrip.onMoveEnded={ [weak self] in self?.snapAfterMove() }
        carryGrip.setAccessibilityIdentifier("float.carry");carryGrip.translatesAutoresizingMaskIntoConstraints=false;header.addSubview(carryGrip)
        collapseButton.font = .systemFont(ofSize:11,weight:.medium);collapseButton.alignment = .center
        collapseButton.bezelStyle = .inline;collapseButton.isBordered=false;collapseButton.target=self;collapseButton.action=#selector(toggleExpanded)
        collapseButton.setAccessibilityIdentifier("float.collapse");collapseButton.translatesAutoresizingMaskIntoConstraints=false;header.addSubview(collapseButton)
        // The count and chevron form one centered, generous click target.
        NSLayoutConstraint.activate([
            close.leadingAnchor.constraint(equalTo:header.leadingAnchor,constant:9),close.topAnchor.constraint(equalTo:header.topAnchor,constant:9),
            pinButton.trailingAnchor.constraint(equalTo:header.trailingAnchor,constant:-9),pinButton.topAnchor.constraint(equalTo:header.topAnchor,constant:9),
            carryGrip.centerXAnchor.constraint(equalTo:header.centerXAnchor),carryGrip.topAnchor.constraint(equalTo:header.topAnchor,constant:8),carryGrip.widthAnchor.constraint(equalToConstant:100),carryGrip.heightAnchor.constraint(equalToConstant:100),
            collapseButton.centerXAnchor.constraint(equalTo:header.centerXAnchor),collapseButton.topAnchor.constraint(equalTo:carryGrip.bottomAnchor,constant:2),collapseButton.widthAnchor.constraint(equalToConstant:104),collapseButton.heightAnchor.constraint(equalToConstant:24)
        ])
        close.identifier=NSUserInterfaceItemIdentifier("float.closeControl")
        header.translatesAutoresizingMaskIntoConstraints=false;surface.addSubview(header);headerHeight=header.heightAnchor.constraint(equalToConstant:126)
        flow.minimumInteritemSpacing=0;flow.minimumLineSpacing=4;flow.sectionInset=NSEdgeInsets(top:8,left:10,bottom:10,right:10)
        collection.collectionViewLayout=flow;collection.dataSource=self;collection.delegate=self;collection.isSelectable=true;collection.allowsMultipleSelection=true;collection.allowsEmptySelection=true;collection.backgroundColors=[.clear]
        collection.register(ShelfCollectionItem.self,forItemWithIdentifier:NSUserInterfaceItemIdentifier("FloatFile"))
        collection.registerForDraggedTypes([.fileURL]);collection.setDraggingSourceOperationMask([.copy,.move,.link],forLocal:false);collection.setDraggingSourceOperationMask([.copy,.move],forLocal:true)
        collection.onDoubleClick={ [weak self] in self?.openSelection() }
        collection.onKey={ [weak self] event in self?.handleKey(event) ?? false }
        collection.contextMenu={ [weak self] in self?.makeContextMenu() }
        collection.frame=NSRect(x:0,y:0,width:156,height:230);collection.autoresizingMask=[.width]
        collection.setAccessibilityIdentifier("float.files");collection.setAccessibilityLabel(L10n.text("Files in Float"))
        scroll.documentView=collection;scroll.hasVerticalScroller=true;scroll.hasHorizontalScroller=false;scroll.autohidesScrollers=true;scroll.drawsBackground=false;scroll.borderType = .noBorder
        scroll.translatesAutoresizingMaskIntoConstraints=false;surface.addSubview(scroll)
        statusLabel.font = .systemFont(ofSize:9);statusLabel.textColor = .secondaryLabelColor;statusLabel.alignment = .center
        statusLabel.translatesAutoresizingMaskIntoConstraints=false;surface.addSubview(statusLabel);statusHeight=statusLabel.heightAnchor.constraint(equalToConstant:0)
        NSLayoutConstraint.activate([
            header.leadingAnchor.constraint(equalTo:surface.leadingAnchor),header.trailingAnchor.constraint(equalTo:surface.trailingAnchor),header.topAnchor.constraint(equalTo:surface.topAnchor),headerHeight,
            scroll.leadingAnchor.constraint(equalTo:surface.leadingAnchor),scroll.trailingAnchor.constraint(equalTo:surface.trailingAnchor),scroll.topAnchor.constraint(equalTo:header.bottomAnchor),scroll.bottomAnchor.constraint(equalTo:statusLabel.topAnchor),
            statusLabel.leadingAnchor.constraint(equalTo:surface.leadingAnchor,constant:8),statusLabel.trailingAnchor.constraint(equalTo:surface.trailingAnchor,constant:-8),statusLabel.bottomAnchor.constraint(equalTo:surface.bottomAnchor),statusHeight
        ])
        updatePresentation()
    }
    private func updatePresentation() {
        glass.isHidden = !isExpanded;glass.frame=surface.bounds
        scroll.isHidden = !isExpanded;statusLabel.isHidden = !isExpanded;pinButton.isHidden = !isExpanded
        header.subviews.first{$0.identifier?.rawValue=="float.closeControl"}?.isHidden = !isExpanded
        headerHeight.constant=isExpanded ? 140:136;statusHeight.constant=isExpanded ? 22:0
        collapseButton.wantsLayer=true;collapseButton.layer?.cornerRadius=11
        collapseButton.layer?.backgroundColor=(isExpanded ? NSColor.clear:NSColor.windowBackgroundColor.withAlphaComponent(0.9)).cgColor
        window?.hasShadow=isExpanded
    }
    private func scheduleHover(_ inside:Bool) {
        hoverTask?.cancel()
        hoverTask=Task { [weak self] in
            try? await Task.sleep(nanoseconds:inside ? 200_000_000:650_000_000)
            guard !Task.isCancelled,let self,!self.interacting,NSEvent.pressedMouseButtons==0,let window=self.window else { return }
            let isInside=window.frame.contains(NSEvent.mouseLocation)
            if inside && isInside { self.setExpanded(true) }
            else if !inside && !isInside { self.setExpanded(false) }
        }
    }
    @objc private func showOptions(_ sender:NSButton) {
        interacting=true;hoverTask?.cancel()
        makeContextMenu().popUp(positioning:nil,at:NSPoint(x:0,y:sender.bounds.minY),in:sender)
        interacting=false;scheduleHover(false)
    }
    private func button(_ symbol:String,help:String,action:Selector)->NSButton {
        let result=NSButton(image:NSImage(systemSymbolName:symbol,accessibilityDescription:help)!,target:self,action:action)
        result.bezelStyle = .inline;result.isBordered=false;result.toolTip=help;result.widthAnchor.constraint(equalToConstant:20).isActive=true;return result
    }
    private func reload(preserving ids:Set<UUID>=[]) {
        flow.itemHeight=viewMode == .list ? 48:108
        collection.reloadData();collection.selectionIndexPaths=Set(items.enumerated().filter{ids.contains($0.element.id)}.map{IndexPath(item:$0.offset,section:0)})
        flow.invalidateLayout();updateLabels();updatePreviewIfVisible()
    }
    private func updateLabels() {
        let selected=selectedIndices.count,count=items.count
        collapseButton.title=(isExpanded ? "⌃  ":"⌄  ")+L10n.text(count==1 ? "1 item":"\(count) items")
        statusLabel.stringValue=L10n.text(selected>0 ? "\(selected) selected":"Space · Preview")
        collection.emptyMessage=count==0 ? L10n.text("Drop files here"):""
        carryGrip.fileURLs=urls;carryGrip.needsDisplay=true
        carryGrip.setAccessibilityLabel(L10n.text(isExpanded ? "Collapse Float":"Expand Float"))
        carryGrip.toolTip=L10n.text("Drag the stack to carry all files. Drag the paperclip to move Float.")
        collapseButton.toolTip=L10n.text(isExpanded ? "Collapse Float":"Expand Float")
        collapseButton.setAccessibilityLabel(L10n.text(isExpanded ? "Collapse Float":"Expand Float")+" · \(count)")
        window?.title="Float · \(count)";window?.invalidateShadow()
    }
    private func setFrame(_ frame:NSRect,animate:Bool) {
        isAdjustingFrame=true;defer { isAdjustingFrame=false }
        window?.setFrame(frame,display:true,animate:animate && NashiMotion.enabled)
    }
    private func snapAfterMove() {
        guard let window,let screen=window.screen ?? NSScreen.main else { return }
        let f=window.frame,v=screen.visibleFrame
        let distances:[(FloatShelfEdge,CGFloat)]=[(.left,abs(f.minX-v.minX)),(.right,abs(f.maxX-v.maxX)),(.top,abs(f.maxY-v.maxY)),(.bottom,abs(f.minY-v.minY))]
        if let nearest=distances.min(by:{$0.1<$1.1}),nearest.1<=20 { attach(to:nearest.0) }
        else { attach(to:nil) }
    }
    func windowDidMove(_ notification:Notification) {
        guard !isAdjustingFrame,let edge=attachedEdge,let window,let screen=window.screen else { return }
        let f=window.frame,v=screen.visibleFrame,distance:CGFloat
        switch edge { case .left:distance=abs(f.minX-v.minX);case .right:distance=abs(f.maxX-v.maxX);case .top:distance=abs(f.maxY-v.maxY);case .bottom:distance=abs(f.minY-v.minY) }
        if distance>35 { attachedEdge=nil;updateLabels() }
    }
    func windowDidResize(_ notification:Notification) {
        collection.frame.size.width=scroll.contentSize.width;flow.invalidateLayout()
        if let edge=attachedEdge,!isAdjustingFrame { attach(to:edge) }
    }
    func windowWillClose(_ notification:Notification) {
        guard !isClosed else { return };isClosed=true;hoverTask?.cancel();filePreview.close()
        if QLPreviewPanel.sharedPreviewPanelExists(),let panel=QLPreviewPanel.shared(),panel.delegate === self { panel.orderOut(nil);panel.delegate=nil;panel.dataSource=nil }
        for item in items { item.releaseScope() };items=[];previewItems=[]
        onClose?()
    }

    @objc private func closeShelf() { close() }
    @objc private func toggleExpanded() { setExpanded(!isExpanded) }
    func setExpanded(_ value:Bool,forceLayout:Bool=false) {
        guard (isExpanded != value || forceLayout),let window else { return }
        hoverTask?.cancel();let old=window.frame;isExpanded=value
        (window as? ShelfPanel)?.allowsTucking = attachedEdge == .top && !value
        updatePresentation()
        let visible=(window.screen ?? NSScreen.main)?.visibleFrame ?? old
        let height=min(visible.height-16,CGFloat(162+max(1,min(items.count,5))*(viewMode == .icons ? 112:52)))
        let size=value ? NSSize(width:viewMode == .icons ? 156:284,height:height):NSSize(width:112,height:136)
        var origin=NSPoint(x:old.midX-size.width/2,y:old.maxY-size.height)
        if attachedEdge == .right { origin.x=old.maxX-size.width }
        if attachedEdge == .bottom { origin.y=old.minY }
        origin.x=max(visible.minX,min(visible.maxX-size.width,origin.x));origin.y=max(visible.minY,min(visible.maxY-size.height,origin.y))
        setFrame(NSRect(origin:origin,size:size),animate:true)
        if let edge=attachedEdge { attach(to:edge) }
        updateLabels();surface.layoutSubtreeIfNeeded();flow.invalidateLayout()
        window.makeFirstResponder(value ? collection:carryGrip)
    }
    @objc private func showPinMenu(_ sender:NSButton) { let menu=pinMenu();menu.popUp(positioning:nil,at:NSPoint(x:0,y:sender.bounds.minY),in:sender) }
    private func pinMenu()->NSMenu {
        let menu=NSMenu(title:"Attach to Edge")
        for edge in FloatShelfEdge.allCases {
            let item=NSMenuItem(title:L10n.text(edge.rawValue.capitalized),action:#selector(pinChoice),keyEquivalent:"");item.target=self;item.representedObject=edge.rawValue;item.state=attachedEdge==edge ? .on:.off;menu.addItem(item)
        }
        menu.addItem(.separator());let detach=NSMenuItem(title:L10n.text("Detach"),action:#selector(pinChoice),keyEquivalent:"");detach.target=self;detach.state=attachedEdge==nil ? .on:.off;menu.addItem(detach);return menu
    }
    @objc private func pinChoice(_ sender:NSMenuItem) { attach(to:(sender.representedObject as? String).flatMap(FloatShelfEdge.init(rawValue:))) }

    func numberOfSections(in collectionView:NSCollectionView)->Int { 1 }
    func collectionView(_ collectionView:NSCollectionView,numberOfItemsInSection section:Int)->Int { items.count }
    func collectionView(_ collectionView:NSCollectionView,itemForRepresentedObjectAt indexPath:IndexPath)->NSCollectionViewItem {
        let item=collectionView.makeItem(withIdentifier:NSUserInterfaceItemIdentifier("FloatFile"),for:indexPath) as! ShelfCollectionItem
        item.configure(url:items[indexPath.item].url,mode:viewMode);return item
    }
    func collectionView(_ collectionView:NSCollectionView,didSelectItemsAt indexPaths:Set<IndexPath>) { updateLabels();updatePreviewIfVisible() }
    func collectionView(_ collectionView:NSCollectionView,didDeselectItemsAt indexPaths:Set<IndexPath>) { updateLabels();updatePreviewIfVisible() }
    func collectionView(_ collectionView:NSCollectionView,canDragItemsAt indexPaths:Set<IndexPath>,with event:NSEvent)->Bool { !indexPaths.isEmpty }
    func collectionView(_ collectionView:NSCollectionView,pasteboardWriterForItemAt indexPath:IndexPath)->NSPasteboardWriting? { items[indexPath.item].url as NSURL }
    func collectionView(_ collectionView:NSCollectionView,draggingSession session:NSDraggingSession,endedAt screenPoint:NSPoint,dragOperation operation:NSDragOperation) { refreshAfterDrag() }
    func collectionView(_ collectionView:NSCollectionView,validateDrop draggingInfo:NSDraggingInfo,proposedIndexPath proposedDropIndexPath:AutoreleasingUnsafeMutablePointer<NSIndexPath>,dropOperation proposedDropOperation:UnsafeMutablePointer<NSCollectionView.DropOperation>)->NSDragOperation {
        if let source=draggingInfo.draggingSource as? NSCollectionView,source === collectionView { return [] }
        guard !ShelfDropSurface.fileURLs(draggingInfo.draggingPasteboard).isEmpty else { return [] }
        proposedDropIndexPath.pointee=IndexPath(item:items.count,section:0) as NSIndexPath;proposedDropOperation.pointee = .before;return .copy
    }
    func collectionView(_ collectionView:NSCollectionView,acceptDrop draggingInfo:NSDraggingInfo,indexPath:IndexPath,dropOperation:NSCollectionView.DropOperation)->Bool {
        let values=ShelfDropSurface.fileURLs(draggingInfo.draggingPasteboard);guard !values.isEmpty else { return false };append(values);return true
    }
    private func refreshAfterDrag() {
        let selected=Set(selectedIndices.map{items[$0].id})
        reload(preserving:selected)
    }

    private func handleKey(_ event:NSEvent)->Bool {
        let command=event.modifierFlags.contains(.command)
        if command {
            switch event.charactersIgnoringModifiers?.lowercased() {
            case "a":setExpanded(true);collection.selectAll(nil);return true
            case "c":if event.modifierFlags.contains(.option) { copyPaths() } else { copyFiles() };return true
            case "v":append(ShelfDropSurface.fileURLs(.general));return true
            case "o":openSelection();return true
            case "r":revealSelection();return true
            default:return false
            }
        }
        if event.keyCode==49 { showQuickLook();return true }
        if event.keyCode==36 || event.keyCode==76 { openSelection();return true }
        if event.keyCode==51 || event.keyCode==117 { removeSelection();return true }
        if event.keyCode==53 { if !collection.selectionIndexPaths.isEmpty { collection.selectionIndexPaths=[];updateLabels() } else { setExpanded(false) };return true }
        guard !event.modifierFlags.contains(.control),!event.modifierFlags.contains(.option),let value=event.characters,!value.isEmpty,value.unicodeScalars.allSatisfy({CharacterSet.alphanumerics.contains($0) || $0==" "}) else { return false }
        let time=ProcessInfo.processInfo.systemUptime,query=value.lowercased()
        if time-lastTypeTime>1 { typeBuffer="" };lastTypeTime=time
        let repeated=typeBuffer==query
        typeBuffer=repeated ? query:typeBuffer+query
        let start=repeated ? (selectedIndices.first ?? -1)+1:0
        for offset in items.indices {
            let index=(start+offset)%items.count
            if items[index].url.lastPathComponent.lowercased().hasPrefix(typeBuffer) {
                let path=IndexPath(item:index,section:0)
                collection.selectionIndexPaths=[path];collection.scrollToItems(at:[path],scrollPosition:.centeredVertically)
                updateLabels();updatePreviewIfVisible();return true
            }
        }
        return !items.isEmpty
    }

    private func makeContextMenu()->NSMenu {
        let menu=NSMenu(title:"Float");menu.delegate=self
        let commands:[(String,Selector)]=[("Open",#selector(openSelection)),("Quick Look",#selector(showQuickLook)),("Show in Finder",#selector(revealSelection)),("Copy",#selector(copyFiles)),("Copy Path",#selector(copyPaths)),("Share…",#selector(shareSelection)),("Remove from Float",#selector(removeSelection))]
        for (title,action) in commands { let item=NSMenuItem(title:L10n.text(title),action:action,keyEquivalent:"");item.target=self;menu.addItem(item) }
        menu.addItem(.separator())
        for (title,mode) in [("Icons",FloatShelfViewMode.icons),("List",.list)] {
            let item=NSMenuItem(title:L10n.text(title),action:#selector(selectViewMode),keyEquivalent:"");item.target=self;item.tag=mode.rawValue;item.state=viewMode==mode ? .on:.off;menu.addItem(item)
        }
        let add=NSMenuItem(title:L10n.text("Add Files…"),action:#selector(chooseFiles),keyEquivalent:"");add.target=self;menu.addItem(add)
        let clear=NSMenuItem(title:L10n.text("Clear Float"),action:#selector(clearShelf),keyEquivalent:"");clear.target=self;menu.addItem(clear)
        let pin=NSMenuItem(title:L10n.text("Attach to Edge"),action:nil,keyEquivalent:"");pin.submenu=pinMenu();menu.addItem(pin)
        return menu
    }
    @objc private func selectViewMode(_ sender:NSMenuItem) {
        setViewMode(FloatShelfViewMode(rawValue:sender.tag) ?? .icons)
    }
    func setViewMode(_ mode:FloatShelfViewMode) {
        let selected=Set(selectedIndices.map{items[$0].id});viewMode=mode
        if isExpanded { setExpanded(false) }
        reload(preserving:selected);setExpanded(true)
    }
    func menuWillOpen(_ menu:NSMenu) { interacting=true;hoverTask?.cancel() }
    func menuDidClose(_ menu:NSMenu) { interacting=false;scheduleHover(false) }
    func validateMenuItem(_ menuItem:NSMenuItem)->Bool {
        if menuItem.action == #selector(chooseFiles) || menuItem.action == #selector(selectViewMode) { return true }
        if menuItem.action == #selector(removeSelection) { return !selectedIndices.isEmpty }
        if menuItem.action == #selector(pinChoice) { return true }
        return !items.isEmpty
    }
    @objc private func openSelection() {
        for url in actionURLs { if !NSWorkspace.shared.open(url) { statusLabel.stringValue="Could not open \(url.lastPathComponent)." } }
    }
    @objc private func revealSelection() { if !actionURLs.isEmpty { NSWorkspace.shared.activateFileViewerSelecting(actionURLs) } }
    @objc private func copyFiles() { NSPasteboard.general.clearContents();NSPasteboard.general.writeObjects(actionURLs.map{$0 as NSURL}) }
    @objc private func copyPaths() { NSPasteboard.general.clearContents();NSPasteboard.general.setString(actionURLs.map(\.path).joined(separator:"\n"),forType:.string) }
    @objc private func shareSelection() {
        guard !actionURLs.isEmpty else { return }
        sharingPicker=NSSharingServicePicker(items:actionURLs);sharingPicker?.show(relativeTo:carryGrip.bounds,of:carryGrip,preferredEdge:.minY)
    }
    @objc private func chooseFiles() {
        let panel=NSOpenPanel();panel.canChooseDirectories=true;panel.canChooseFiles=true;panel.allowsMultipleSelection=true;panel.prompt=L10n.text("Add to Float")
        if panel.runModal() == .OK { append(panel.urls) }
    }
    @objc private func removeSelection() {
        let selected=Set(selectedIndices)
        for index in selected { items[index].releaseScope() }
        items=items.enumerated().filter{!selected.contains($0.offset)}.map(\.element);reload();if isExpanded { setExpanded(true,forceLayout:true) }
    }
    @objc private func clearShelf() { for item in items { item.releaseScope() };items=[];reload();if isExpanded { setExpanded(true,forceLayout:true) } }
    @objc private func showQuickLook() {filePreview.show(actionURLs)}
    private func updatePreviewIfVisible() {
        guard QLPreviewPanel.sharedPreviewPanelExists(),let panel=QLPreviewPanel.shared(),panel.isVisible,panel.delegate === self else { return }
        previewItems=actionURLs.map(ShelfPreviewItem.init);panel.reloadData()
        if previewItems.isEmpty { panel.orderOut(nil) }
    }
    override func acceptsPreviewPanelControl(_ panel:QLPreviewPanel!)->Bool { !items.isEmpty }
    override func beginPreviewPanelControl(_ panel:QLPreviewPanel!) { panel.dataSource=self;panel.delegate=self }
    override func endPreviewPanelControl(_ panel:QLPreviewPanel!) { panel.dataSource=nil;panel.delegate=nil }
    func numberOfPreviewItems(in panel:QLPreviewPanel!)->Int { previewItems.count }
    func previewPanel(_ panel:QLPreviewPanel!,previewItemAt index:Int)->QLPreviewItem! { previewItems.indices.contains(index) ? previewItems[index]:nil }
    func previewPanel(_ panel:QLPreviewPanel!,handle event:NSEvent!)->Bool {
        if event.type == .keyDown,event.keyCode==49 { panel.orderOut(nil);return true };return false
    }
}

@MainActor private final class ShelfFile {
    let id=UUID()
    let url:URL
    private var scoped:Bool
    init(url:URL) { self.url=url;scoped=url.startAccessingSecurityScopedResource() }
    deinit { if scoped { url.stopAccessingSecurityScopedResource() } }
    func releaseScope() { if scoped { url.stopAccessingSecurityScopedResource();scoped=false } }
}
private final class ShelfPreviewItem:NSObject,QLPreviewItem {
    let url:URL
    init(_ url:URL) { self.url=url }
    var previewItemURL:URL? { url }
    var previewItemTitle:String? { url.lastPathComponent }
}
private final class ShelfPanel:NSPanel {
    var allowsTucking=false
    override func constrainFrameRect(_ frameRect:NSRect,to screen:NSScreen?)->NSRect { allowsTucking ? frameRect:super.constrainFrameRect(frameRect,to:screen) }
    var onKey:((NSEvent)->Bool)?
    override func keyDown(with event:NSEvent) { if onKey?(event) != true { super.keyDown(with:event) } }
    override var canBecomeKey:Bool { true }
    override var canBecomeMain:Bool { false }
}
@MainActor private final class ShelfBackdrop:NSVisualEffectView {
    private let tint=CAGradientLayer()
    override init(frame frameRect:NSRect) { super.init(frame:frameRect);wantsLayer=true;layer?.addSublayer(tint);updateTint() }
    required init?(coder:NSCoder) { super.init(coder:coder) }
    override func viewDidChangeEffectiveAppearance() { super.viewDidChangeEffectiveAppearance();updateTint() }
    override func layout() { super.layout();tint.frame=bounds }
    private func updateTint() {
        let dark=effectiveAppearance.bestMatch(from:[.darkAqua,.aqua]) == .darkAqua
        tint.colors=dark ? [NSColor(srgbRed:0.23,green:0.25,blue:0.18,alpha:0.75).cgColor,NSColor(srgbRed:0.10,green:0.16,blue:0.12,alpha:0.8).cgColor]:[NSColor(srgbRed:0.98,green:0.89,blue:0.71,alpha:0.72).cgColor,NSColor(srgbRed:0.85,green:0.91,blue:0.79,alpha:0.72).cgColor]
        tint.startPoint=CGPoint(x:0.5,y:1);tint.endPoint=CGPoint(x:0.5,y:0)
    }
}
@MainActor private final class ShelfMoveHeader:NSView {
    var onMoveEnded:(()->Void)?
    var onMoving:((Bool)->Void)?
    override func mouseDown(with event:NSEvent) { onMoving?(true);window?.performDrag(with:event);onMoving?(false);onMoveEnded?() }
}
@MainActor private final class ShelfDropSurface:NSView {
    var onHover:((Bool)->Void)?
    private var tracking:NSTrackingArea?
    override func updateTrackingAreas() {
        if let tracking { removeTrackingArea(tracking) };let area=NSTrackingArea(rect:.zero,options:[.mouseEnteredAndExited,.activeAlways,.inVisibleRect],owner:self,userInfo:nil);tracking=area;addTrackingArea(area);super.updateTrackingAreas()
    }
    override func mouseEntered(with event:NSEvent) { onHover?(true) }
    override func mouseExited(with event:NSEvent) { onHover?(false) }
    var onFilesDropped:(([URL])->Void)?
    override init(frame frameRect:NSRect) { super.init(frame:frameRect);registerForDraggedTypes([.fileURL]) }
    required init?(coder:NSCoder) { super.init(coder:coder);registerForDraggedTypes([.fileURL]) }
    static func fileURLs(_ pasteboard:NSPasteboard)->[URL] { (pasteboard.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL]) ?? [] }
    override func draggingEntered(_ sender:NSDraggingInfo)->NSDragOperation { Self.fileURLs(sender.draggingPasteboard).isEmpty ? []:.copy }
    override func draggingUpdated(_ sender:NSDraggingInfo)->NSDragOperation { draggingEntered(sender) }
    override func prepareForDragOperation(_ sender:NSDraggingInfo)->Bool { !Self.fileURLs(sender.draggingPasteboard).isEmpty }
    override func performDragOperation(_ sender:NSDraggingInfo)->Bool { let urls=Self.fileURLs(sender.draggingPasteboard);guard !urls.isEmpty else { return false };onFilesDropped?(urls);return true }
}
/// A vertical shelf never wraps into a second column. Unlike a flow layout,
/// it can resize through compact intermediate widths without stale item metrics.
@MainActor private final class ShelfColumnLayout:NSCollectionViewLayout {
    var itemHeight:CGFloat=108
    var minimumInteritemSpacing:CGFloat=0
    var minimumLineSpacing:CGFloat=4
    var sectionInset=NSEdgeInsets(top:8,left:10,bottom:10,right:10)
    private var attributes:[NSCollectionViewLayoutAttributes]=[]
    private var contentSize=NSSize.zero
    override func prepare() {
        super.prepare();guard let collectionView else { return }
        let width=max(1,collectionView.bounds.width),count=collectionView.numberOfItems(inSection:0)
        attributes=(0..<count).map { index in
            let value=NSCollectionViewLayoutAttributes(forItemWith:IndexPath(item:index,section:0))
            value.frame=NSRect(x:sectionInset.left,y:sectionInset.top+CGFloat(index)*(itemHeight+minimumLineSpacing),width:max(1,width-sectionInset.left-sectionInset.right),height:itemHeight);return value
        }
        contentSize=NSSize(width:width,height:max(collectionView.bounds.height,sectionInset.top+CGFloat(count)*(itemHeight+minimumLineSpacing)+sectionInset.bottom))
    }
    override var collectionViewContentSize:NSSize { contentSize }
    override func layoutAttributesForElements(in rect:NSRect)->[NSCollectionViewLayoutAttributes] { attributes.filter{$0.frame.intersects(rect)} }
    override func layoutAttributesForItem(at indexPath:IndexPath)->NSCollectionViewLayoutAttributes? { attributes.indices.contains(indexPath.item) ? attributes[indexPath.item]:nil }
    override func shouldInvalidateLayout(forBoundsChange newBounds:NSRect)->Bool { newBounds.size != collectionView?.bounds.size }
}
@MainActor private final class ShelfCollectionView:NSCollectionView {
    var onDoubleClick:(()->Void)?
    var onKey:((NSEvent)->Bool)?
    var contextMenu:(()->NSMenu?)?
    var emptyMessage="" { didSet { needsDisplay=true } }
    override func keyDown(with event:NSEvent) { if onKey?(event) != true { super.keyDown(with:event) } }
    override func mouseDown(with event:NSEvent) {
        let clickedItem=indexPathForItem(at:convert(event.locationInWindow,from:nil)) != nil
        super.mouseDown(with:event);if event.clickCount==2,clickedItem { onDoubleClick?() }
    }
    override func menu(for event:NSEvent)->NSMenu? {
        let point=convert(event.locationInWindow,from:nil)
        if let path=indexPathForItem(at:point),!selectionIndexPaths.contains(path) { selectionIndexPaths=[path] }
        return contextMenu?()
    }
    override func layout() {
        super.layout()
        if let clip=superview as? NSClipView,frame.height<clip.bounds.height { frame.size.height=clip.bounds.height }
    }
    override func draw(_ dirtyRect:NSRect) {
        super.draw(dirtyRect)
        if !emptyMessage.isEmpty {
            let paragraph=NSMutableParagraphStyle();paragraph.alignment = .center
            (emptyMessage as NSString).draw(in:NSRect(x:15,y:bounds.midY-10,width:max(0,bounds.width-30),height:35),withAttributes:[.font:NSFont.systemFont(ofSize:12),.foregroundColor:NSColor.secondaryLabelColor,.paragraphStyle:paragraph])
        }
    }
}
@MainActor private final class ShelfCollectionItem:NSCollectionViewItem {
    private let icon=NSImageView()
    private let name=NSTextField(wrappingLabelWithString:"")
    private let detail=NSTextField(labelWithString:"")
    private var url:URL?
    private var mode:FloatShelfViewMode = .icons
    private var thumbnailRequest:QLThumbnailGenerator.Request?
    override var isSelected:Bool { didSet { updateSelection() } }
    override func loadView() {
        view=NSView();view.wantsLayer=true;view.layer?.cornerRadius=13;view.layer?.borderWidth=1
        icon.imageScaling = .scaleProportionallyUpOrDown
        name.font = .systemFont(ofSize:11);name.maximumNumberOfLines=2;name.isSelectable=false;name.lineBreakMode = .byWordWrapping
        detail.font = .systemFont(ofSize:9);detail.textColor = .secondaryLabelColor;detail.lineBreakMode = .byTruncatingMiddle
        view.addSubview(icon);view.addSubview(name);view.addSubview(detail)
        view.setAccessibilityElement(true);view.setAccessibilityRole(.button)
    }
    override func viewDidLayout() {
        super.viewDidLayout()
        if mode == .icons {
            icon.frame=NSRect(x:(view.bounds.width-60)/2,y:43,width:60,height:60)
            name.alignment = .center;name.frame=NSRect(x:4,y:3,width:max(0,view.bounds.width-8),height:35);detail.isHidden=true
        } else {
            icon.frame=NSRect(x:7,y:8,width:32,height:32);name.alignment = .left;name.frame=NSRect(x:49,y:24,width:max(0,view.bounds.width-57),height:17)
            detail.isHidden=false;detail.frame=NSRect(x:49,y:7,width:max(0,view.bounds.width-57),height:14)
        }
    }
    func configure(url:URL,mode:FloatShelfViewMode) {
        _=view;self.url=url;self.mode=mode
        if let request=thumbnailRequest { QLThumbnailGenerator.shared.cancel(request) }
        name.stringValue=url.lastPathComponent;detail.stringValue=url.deletingLastPathComponent().path;icon.image=NSWorkspace.shared.icon(forFile:url.path)
        view.toolTip=url.path;view.setAccessibilityLabel(url.lastPathComponent);view.needsLayout=true;updateSelection()
        let request=QLThumbnailGenerator.Request(fileAt:url,size:NSSize(width:96,height:96),scale:NSScreen.main?.backingScaleFactor ?? 2,representationTypes:.all)
        thumbnailRequest=request
        QLThumbnailGenerator.shared.generateBestRepresentation(for:request) { [weak self] representation,_ in
            DispatchQueue.main.async { guard let self,self.url==url,let representation else { return };self.icon.image=representation.nsImage }
        }
    }
    private func updateSelection() {
        guard isViewLoaded else { return }
        view.layer?.backgroundColor=(isSelected ? NashiPalette.gold.withAlphaComponent(0.18):NSColor.clear).cgColor
        view.layer?.borderColor=(isSelected ? NashiPalette.gold.withAlphaComponent(0.7):NSColor.clear).cgColor
        view.setAccessibilitySelected(isSelected)
    }
}
/// The files are the drag target; the visible paperclip is a separate window grip.
@MainActor private final class ShelfCarryGrip:NSView,NSDraggingSource {
    var urls:(()->[URL])?
    var onDragEnded:(()->Void)?
    var contextMenu:(()->NSMenu?)?
    override func menu(for event:NSEvent)->NSMenu? { contextMenu?() }
    var onClick:(()->Void)?
    var onInteraction:((Bool)->Void)?
    var onMoveEnded:(()->Void)?
    var fileURLs:[URL]=[] { didSet { if oldValue != fileURLs { loadThumbnails() } } }
    private var thumbnails:[URL:NSImage]=[:]
    private var requests:[QLThumbnailGenerator.Request]=[]
    private var origin=NSPoint.zero
    private var sessionActive=false
    private var moved=false
    override var acceptsFirstResponder:Bool { true }
    override init(frame frameRect:NSRect) {
        super.init(frame:frameRect);setAccessibilityElement(true);setAccessibilityRole(.button)
        setAccessibilityLabel(L10n.text("Expand Float"))
    }
    required init?(coder:NSCoder) { super.init(coder:coder) }
    override func accessibilityPerformPress()->Bool { onClick?();return true }
    private func loadThumbnails() {
        for request in requests { QLThumbnailGenerator.shared.cancel(request) };requests=[];thumbnails=[:]
        for url in fileURLs.suffix(3) {
            let request=QLThumbnailGenerator.Request(fileAt:url,size:NSSize(width:96,height:96),scale:window?.backingScaleFactor ?? 2,representationTypes:.all)
            requests.append(request)
            QLThumbnailGenerator.shared.generateBestRepresentation(for:request) { [weak self] result,_ in
                DispatchQueue.main.async { guard let self,self.fileURLs.suffix(3).contains(url),let result else { return };self.thumbnails[url]=result.nsImage;self.needsDisplay=true }
            }
        }
        needsDisplay=true
    }
    override func draw(_ dirtyRect:NSRect) {
        let visible=Array(fileURLs.suffix(3))
        if visible.isEmpty {
            NSImage(systemSymbolName:"tray.and.arrow.down",accessibilityDescription:nil)?.draw(in:NSRect(x:24,y:20,width:52,height:52))
        }
        for (index,url) in visible.enumerated() {
            NSGraphicsContext.saveGraphicsState()
            let offset=CGFloat(visible.count-index-1),transform=AffineTransform(translationByX:bounds.midX,byY:bounds.midY-3)
            var rotated=transform;rotated.rotate(byDegrees:offset * -7);rotated.translate(x:-36+offset * -2,y:-36-offset * 2)
            (rotated as NSAffineTransform).concat()
            let rect=NSRect(x:0,y:0,width:72,height:72)
            let shadow=NSShadow();shadow.shadowColor=NSColor.black.withAlphaComponent(0.2);shadow.shadowBlurRadius=3;shadow.shadowOffset=NSSize(width:0,height:-1);shadow.set()
            let icon=thumbnails[url] ?? NSWorkspace.shared.icon(forFile:url.path)
            icon.draw(in:rect,from:.zero,operation:.sourceOver,fraction:1,respectFlipped:true,hints:[.interpolation:NSImageInterpolation.high])
            NSGraphicsContext.restoreGraphicsState()
        }
        // A silver wire clip keeps the stack recognizable without a surrounding window.
        let clip=NSBezierPath();clip.move(to:NSPoint(x:76,y:29));clip.line(to:NSPoint(x:57,y:72))
        clip.curve(to:NSPoint(x:78,y:81),controlPoint1:NSPoint(x:47,y:94),controlPoint2:NSPoint(x:70,y:101))
        clip.line(to:NSPoint(x:94,y:43));clip.curve(to:NSPoint(x:81,y:37),controlPoint1:NSPoint(x:102,y:26),controlPoint2:NSPoint(x:87,y:22))
        clip.line(to:NSPoint(x:66,y:72));clip.lineCapStyle = .round
        NSColor.black.withAlphaComponent(0.24).setStroke();clip.lineWidth=4.5;clip.stroke()
        NSColor(srgbRed:0.76,green:0.79,blue:0.77,alpha:1).setStroke();clip.lineWidth=2.6;clip.stroke()
        NSColor.white.withAlphaComponent(0.8).setStroke();clip.lineWidth=0.7;clip.stroke()
    }
    override func mouseDown(with event:NSEvent) { origin=convert(event.locationInWindow,from:nil);moved=false;onInteraction?(true) }
    override func mouseUp(with event:NSEvent) { onInteraction?(false);if !moved && !sessionActive { onClick?() } }
    override func mouseDragged(with event:NSEvent) {
        let point=convert(event.locationInWindow,from:nil),values=urls?() ?? []
        guard !sessionActive,hypot(point.x-origin.x,point.y-origin.y)>3 else { return };moved=true
        if origin.x>62 && origin.y>40 {
            window?.performDrag(with:event);onInteraction?(false);onMoveEnded?();return
        }
        guard !values.isEmpty else { return }
        let items=values.enumerated().map { index,url in
            let item=NSDraggingItem(pasteboardWriter:url as NSURL),image=(thumbnails[url] ?? NSWorkspace.shared.icon(forFile:url.path)).copy() as! NSImage;image.size=NSSize(width:42,height:42)
            item.setDraggingFrame(NSRect(x:point.x+CGFloat(index%5)*3,y:point.y+CGFloat(index%5)*3,width:42,height:42),contents:image);return item
        }
        sessionActive=true;beginDraggingSession(with:items,event:event,source:self)
    }
    func draggingSession(_ session:NSDraggingSession,sourceOperationMaskFor context:NSDraggingContext)->NSDragOperation { [.copy,.move,.link] }
    func draggingSession(_ session:NSDraggingSession,endedAt screenPoint:NSPoint,operation:NSDragOperation) { sessionActive=false;onInteraction?(false);onDragEnded?() }
}
