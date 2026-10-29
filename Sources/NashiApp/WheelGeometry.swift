import AppKit

struct WheelGeometry {
    let bounds: NSRect
    let count: Int
    var scale: CGFloat { min(bounds.width, bounds.height) / 320 }
    var center: NSPoint { NSPoint(x: bounds.midX, y: bounds.midY) }
    var radius: CGFloat { min(bounds.width, bounds.height) * 0.44 }
    var inner: CGFloat { radius * 0.407 }
    var startAngle: CGFloat { count == 3 ? 180 : 90 }
    var disc: NSBezierPath {
        let r = inner - 4 * scale
        return NSBezierPath(ovalIn: NSRect(x: center.x-r, y: center.y-r, width: 2*r, height: 2*r))
    }
    func labelCenters(_ index: Int) -> [NSPoint] {
        let angle = (startAngle - CGFloat(index) * 360 / CGFloat(max(1,count))) * .pi / 180
        // The area center moves inward as petals get wider. A fixed radius
        // centers eight-item menus but pushes five-item PDF labels outward.
        // Use the centroid of the annular sector, including its angular gap.
        let sweep = (360 / CGFloat(max(1,count)) - 1.8) * .pi / 180
        let centroid = count > 1
            ? 4 * sin(sweep/2) * (pow(radius,3)-pow(inner,3)) / (3*sweep*(radius*radius-inner*inner))
            : (radius+inner)/2
        let preferred = centroid/radius
        var nearby: [CGFloat] = [preferred]
        for step in 1...12 {
            let distance = CGFloat(step) * CGFloat(0.02)
            nearby.append(preferred + distance)
            nearby.append(preferred - distance)
        }
        var fractions: [CGFloat] = nearby.filter { $0 > inner/radius && $0 < 0.86 }
        fractions.append(contentsOf: [0.73,0.75,0.71,0.77,0.69,0.79,0.67,0.81])
        var result: [NSPoint] = []
        let cosine = cos(angle), sine = sin(angle)
        let offsets: [CGFloat] = [0, -4, 4, -8, 8]
        for fraction in fractions {
            let radial = radius * fraction
            for offset in offsets {
                let tangent = offset * scale
                result.append(NSPoint(x: center.x + radial*cosine - tangent*sine,
                                      y: center.y + radial*sine + tangent*cosine))
            }
        }
        return result
    }
    func petal(_ index: Int) -> NSBezierPath {
        let delta = 360 / CGFloat(max(1,count)), gap: CGFloat = 0.9
        let mid = startAngle - CGFloat(index)*delta, a = mid-delta/2+gap, b = mid+delta/2-gap
        let corner = min(16*scale, inner*delta * .pi/180/2.6)
        let outerInset = corner/radius*180 / .pi, innerInset = corner/inner*180 / .pi
        func point(_ r: CGFloat, _ degrees: CGFloat) -> NSPoint {
            NSPoint(x: center.x+r*cos(degrees * .pi/180), y: center.y+r*sin(degrees * .pi/180))
        }
        let path = NSBezierPath()
        path.move(to: point(radius-corner,a))
        path.curve(to: point(radius,a+outerInset), controlPoint1: point(radius,a), controlPoint2: point(radius,a))
        path.appendArc(withCenter: center, radius: radius, startAngle: a+outerInset, endAngle: b-outerInset, clockwise: false)
        path.curve(to: point(radius-corner,b), controlPoint1: point(radius,b), controlPoint2: point(radius,b))
        path.line(to: point(inner+corner,b))
        path.curve(to: point(inner,b-innerInset), controlPoint1: point(inner,b), controlPoint2: point(inner,b))
        path.appendArc(withCenter: center, radius: inner, startAngle: b-innerInset, endAngle: a+innerInset, clockwise: true)
        path.curve(to: point(inner+corner,a), controlPoint1: point(inner,a), controlPoint2: point(inner,a))
        path.close()
        return path
    }
}
