import AppKit

/// Shared vector artwork: the app, menu bar, installer icon and brand exports
/// all draw the same round Asian pear, stem and leaf.
enum NashiArtwork {
    static let gold=NSColor(srgbRed:0.89,green:0.72,blue:0.35,alpha:1)
    static let ink=NSColor(srgbRed:0.20,green:0.25,blue:0.14,alpha:1)
    static func bodyPath()->CGPath {
        let p=CGMutablePath();p.move(to:CGPoint(x:0.50,y:0.235))
        p.addCurve(to:CGPoint(x:0.865,y:0.36),control1:CGPoint(x:0.60,y:0.17),control2:CGPoint(x:0.79,y:0.23))
        p.addCurve(to:CGPoint(x:0.66,y:0.905),control1:CGPoint(x:0.99,y:0.56),control2:CGPoint(x:0.85,y:0.85))
        p.addCurve(to:CGPoint(x:0.13,y:0.64),control1:CGPoint(x:0.43,y:0.99),control2:CGPoint(x:0.16,y:0.88))
        p.addCurve(to:CGPoint(x:0.43,y:0.228),control1:CGPoint(x:0.08,y:0.39),control2:CGPoint(x:0.26,y:0.18))
        p.addQuadCurve(to:CGPoint(x:0.50,y:0.235),control:CGPoint(x:0.468,y:0.25));p.closeSubpath();return p
    }
    static func leafPath()->CGPath {
        let p=CGMutablePath();p.move(to:CGPoint(x:0.49,y:0.17))
        p.addCurve(to:CGPoint(x:0.74,y:0.115),control1:CGPoint(x:0.53,y:0.03),control2:CGPoint(x:0.73,y:0.065))
        p.addCurve(to:CGPoint(x:0.49,y:0.17),control1:CGPoint(x:0.66,y:0.24),control2:CGPoint(x:0.56,y:0.23));p.closeSubpath();return p
    }
    static func draw(in rect:NSRect,happy:Bool=false,monochrome:Bool=false) {
        guard let c=NSGraphicsContext.current?.cgContext else { return }
        c.saveGState();defer{c.restoreGState()}
        c.translateBy(x:rect.minX,y:rect.maxY);c.scaleBy(x:rect.width,y:-rect.height)
        if monochrome {
            c.setFillColor(NSColor.black.cgColor);c.addPath(bodyPath());c.fillPath();c.addPath(leafPath());c.fillPath()
        } else {
            c.saveGState();c.addPath(bodyPath());c.clip()
            let colors=[NSColor(srgbRed:0.98,green:0.86,blue:0.58,alpha:1).cgColor,gold.cgColor] as CFArray
            let gradient=CGGradient(colorsSpace:CGColorSpace(name:CGColorSpace.sRGB),colors:colors,locations:[0,1])!
            c.drawLinearGradient(gradient,start:CGPoint(x:0.2,y:0.25),end:CGPoint(x:0.85,y:0.95),options:[.drawsBeforeStartLocation,.drawsAfterEndLocation]);c.restoreGState()
            c.setFillColor(NSColor(srgbRed:0.49,green:0.62,blue:0.36,alpha:1).cgColor);c.addPath(leafPath());c.fillPath()
            c.setFillColor(NSColor.white.withAlphaComponent(0.24).cgColor);c.fillEllipse(in:CGRect(x:0.24,y:0.32,width:0.085,height:0.16))
            c.setFillColor(ink.withAlphaComponent(0.19).cgColor)
            for point in [CGPoint(x:0.27,y:0.48),CGPoint(x:0.65,y:0.37),CGPoint(x:0.34,y:0.75),CGPoint(x:0.72,y:0.69),CGPoint(x:0.21,y:0.61),CGPoint(x:0.77,y:0.5),CGPoint(x:0.57,y:0.82),CGPoint(x:0.68,y:0.77),CGPoint(x:0.38,y:0.34)] { c.fillEllipse(in:CGRect(x:point.x,y:point.y,width:0.016,height:0.016)) }
            c.setFillColor(ink.cgColor)
            for x:CGFloat in [0.39,0.59] { c.addPath(CGPath(roundedRect:CGRect(x:x,y:0.51,width:0.045,height:happy ? 0.035:0.065),cornerWidth:0.0225,cornerHeight:0.0225,transform:nil));c.fillPath() }
            c.setStrokeColor(ink.cgColor);c.setLineWidth(0.028);c.setLineCap(.round);c.move(to:CGPoint(x:0.45,y:0.64));c.addQuadCurve(to:CGPoint(x:0.55,y:0.64),control:CGPoint(x:0.50,y:0.71));c.strokePath()
        }
        c.setStrokeColor((monochrome ? NSColor.black:NSColor(srgbRed:0.38,green:0.29,blue:0.15,alpha:1)).cgColor);c.setLineWidth(0.035);c.setLineCap(.round)
        c.move(to:CGPoint(x:0.49,y:0.24));c.addQuadCurve(to:CGPoint(x:0.47,y:0.12),control:CGPoint(x:0.455,y:0.18));c.strokePath()
    }
    static func mark(size:CGFloat,happy:Bool=false)->NSImage {
        NSImage(size:NSSize(width:size,height:size),flipped:false) { rect in draw(in:rect,happy:happy);return true }
    }
    static func menuIcon(updateAvailable:Bool=false)->NSImage {
        let image=NSImage(size:NSSize(width:20,height:20),flipped:false) { rect in
            draw(in:rect.insetBy(dx:0.5,dy:0.5),monochrome:true)
            if updateAvailable { NSColor.black.setFill();NSBezierPath(ovalIn:NSRect(x:15,y:15,width:4,height:4)).fill() }
            return true
        }
        image.isTemplate=true;image.accessibilityDescription=updateAvailable ? L10n.text("Nashi update available"):"Nashi";return image
    }
}
