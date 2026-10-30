import AppKit
import SwiftUI

/// The real wheel drawn without interaction, for the welcome tour and Settings › Wheels.
struct WheelPreview:NSViewRepresentable {
    let mode:WheelMode
    let items:[WheelItem]
    var backdrop=false
    func makeNSView(context:Context)->NSView {
        let view=WheelView(frame:NSRect(x:0,y:0,width:300,height:300));view.setAccessibilityElement(false);view.autoresizingMask=[.width,.height]
        guard backdrop else { return view }
        let effect=WheelController.makeBackdrop(diameter:300);view.frame=effect.bounds;effect.addSubview(view);return effect
    }
    func updateNSView(_ host:NSView,context:Context) {
        if let effect=host as? NSVisualEffectView { effect.state=WheelController.backdropState;effect.layer?.cornerRadius=min(host.bounds.width,host.bounds.height)/2 }
        guard let view=(host as? WheelView) ?? host.subviews.first as? WheelView else { return }
        view.mode=mode;view.items=items;view.inputs=[];view.selected=nil;view.onSelect=nil;view.onCancel=nil;view.onDragInputs=nil;view.needsDisplay=true
    }
}
