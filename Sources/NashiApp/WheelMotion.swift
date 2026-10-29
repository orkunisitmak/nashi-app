import Foundation
import CoreGraphics

/// User-selectable wheel entrance/exit. Stored under `wheelAnimationStyle`.
enum WheelAnimationStyle:String,CaseIterable,Identifiable {
    case classic,soft,depth,island,bloom,spin,jelly,snap
    static let storageKey="wheelAnimationStyle"
    static var current:WheelAnimationStyle { WheelAnimationStyle(rawValue:UserDefaults.standard.string(forKey:storageKey) ?? "") ?? .depth }
    var id:String { rawValue }
    var title:String {
        switch self {
        case .classic:return "Classic";case .soft:return "Soft";case .depth:return "Pop out";case .island:return "Island";case .bloom:return "Bloom"
        case .spin:return "Spin";case .jelly:return "Jelly";case .snap:return "Snap"
        }
    }
    var summary:String {
        switch self {
        case .classic:return "Fades in quickly and disappears at once."
        case .soft:return "Sharpens from a blur and settles with a gentle spring."
        case .depth:return "Flies out of the screen toward you in 3D and sinks back into it when it closes."
        case .island:return "Grows out of the dragged file’s capsule and folds back into it."
        case .bloom:return "Petals open one by one, clockwise, then fold back."
        case .spin:return "Spins open and spins away."
        case .jelly:return "Wobbles into place like Liquid Glass and pops like a bubble."
        case .snap:return "Turns to dust with a snap and reforms from it. Takes longer."
        }
    }
    /// Milliseconds. Exits are always shorter or equal, as on Apple platforms.
    var openDuration:Double { switch self { case .classic:return 130;case .soft:return 340;case .depth:return 720;case .island:return 560;case .bloom:return 520;case .spin:return 480;case .jelly:return 560;case .snap:return Double(WheelMotion.snapAssemble+WheelMotion.snapCrossfade) } }
    var closeDuration:Double { switch self { case .classic:return 0;case .soft:return 200;case .depth:return 420;case .island:return 510;case .bloom:return 300;case .spin:return 280;case .jelly:return 240;case .snap:return Double(WheelMotion.snapScatter) } }
}

struct PetalMotion:Equatable { var scale:CGFloat=1,rotation:CGFloat=0,alpha:CGFloat=1,offset:CGFloat=0 }

/// One frame of the wheel. Lengths are points at the 320 pt reference size; rotations are clockwise degrees,
/// pitch tips the top edge away from the viewer and yaw turns the right edge away, both in degrees.
struct WheelMotion:Equatable {
    var scale:CGFloat=1,stretchX:CGFloat=1,stretchY:CGFloat=1,rotation:CGFloat=0,alpha:CGFloat=1,blur:CGFloat=0
    /// Distance toward the viewer in points; negative is deeper inside the screen.
    var pitch:CGFloat=0,yaw:CGFloat=0,lift:CGFloat=0
    /// Island morph: glass base size in points, nil means the full circle.
    var baseSize:CGSize?=nil
    var baseScale:CGFloat=1,baseAlpha:CGFloat=1,capsuleLabel:CGFloat=0
    var petals:[PetalMotion]
    var discScale:CGFloat=1,discAlpha:CGFloat=1
    var barAlpha:CGFloat=1,barOffset:CGFloat=0
    /// Snap: alpha of the dust layer while it hands over to the live wheel.
    var dustAlpha:CGFloat=0

    static let snapLayers=24,snapAssemble=720+28*(snapLayers-1),snapCrossfade=160,snapScatter=850+30*(snapLayers-1)
    static func rest(_ count:Int)->WheelMotion { WheelMotion(petals:Array(repeating:PetalMotion(),count:count)) }

    /// `style == nil` is the Reduce Motion fallback: a 150 ms fade both ways.
    static func frame(_ style:WheelAnimationStyle?,opening:Bool,progress:Double,count:Int)->WheelMotion {
        var m=rest(count);let p=CGFloat(clamp(progress)),n=max(1,count)
        guard let style else { m.alpha=opening ? p:1-p;m.barAlpha=m.alpha;return m }
        let t=p*CGFloat(opening ? style.openDuration:style.closeDuration)
        func stage(_ start:CGFloat,_ length:CGFloat)->CGFloat { clamp((t-start)/length) }
        switch (style,opening) {
        case (.classic,true):m.alpha=p;m.barAlpha=p
        case (.classic,false):m.alpha=0;m.barAlpha=0
        case (.soft,true):
            let s=spring(p,0.8),e=easeOut(p);m.scale=0.9+0.1*s;m.alpha=clamp(p*2.4);m.blur=9*(1-e);m.barAlpha=e;m.barOffset=8*(1-s)
        case (.soft,false):
            let e=easeIn(p);m.scale=1-0.07*e;m.alpha=1-p;m.blur=6*p;m.barAlpha=1-p;m.barOffset=6*e
        case (.depth,true):
            // Flies out of the depth of the screen, overshoots a little toward the viewer, settles.
            let s=spring(p,0.62);m.lift = -900*(1-s);m.pitch=18*(1-s);m.scale=0.6+0.4*s;m.alpha=clamp(p*4)
            m.barAlpha=easeOut(clamp((p-0.4)/0.6));m.barOffset=10*(1-min(1,s))
        case (.depth,false):
            let e=easeIn(p);m.lift = -700*e;m.pitch=16*e;m.scale=1-0.4*e;m.alpha=1-p*p;m.barAlpha=1-clamp(p*2);m.barOffset=8*e
        case (.island,true):
            let pop=stage(0,140),morph=stage(140,420),s=spring(morph,0.72)
            m.baseScale=0.6+0.4*spring(pop,0.7);m.baseAlpha=clamp(pop*2)
            m.baseSize=morph>=1 ? nil:CGSize(width:lerp(132,310,s),height:lerp(34,310,s))
            m.capsuleLabel=clamp(1-morph*3)
            for i in 0..<count { let l=stagger(clamp((morph-0.12)/0.88),i,n,0.3);m.petals[i].alpha=clamp(l*2);m.petals[i].offset = -16*(1-spring(l,0.8)) }
            let d=clamp((morph-0.25)/0.6);m.discAlpha=easeOut(d);m.discScale=0.85+0.15*spring(d,0.75);m.barAlpha=easeOut(clamp((morph-0.4)/0.6))
        case (.island,false):
            let fold=stage(0,170),shrink=stage(170,220),fade=stage(390,120),e=easeInOut(shrink)
            for i in 0..<count { let l=stagger(fold,count-1-i,n,0.3);m.petals[i].alpha=1-l;m.petals[i].offset = -12*easeIn(l) }
            m.discAlpha=1-fold;m.discScale=1-0.15*easeIn(fold);m.barAlpha=1-fold
            m.baseSize=shrink>0 ? CGSize(width:lerp(310,132,e),height:lerp(310,34,e)):nil
            m.capsuleLabel=clamp((shrink-0.5)*2);m.baseAlpha=1-fade;m.baseScale=1-0.25*easeIn(fade)
        case (.bloom,true):
            m.baseScale=0.4+0.6*spring(clamp(p*1.7),0.72);m.baseAlpha=clamp(p*5)
            for i in 0..<count { let l=stagger(clamp((p-0.08)/0.92),i,n,0.45),s=spring(l,0.62);m.petals[i]=PetalMotion(scale:0.3+0.7*s,rotation:-25*(1-s),alpha:clamp(l*2.5)) }
            let d=clamp(p*2.2-0.1);m.discScale=0.5+0.5*spring(d,0.6);m.discAlpha=clamp(d*3);m.barAlpha=easeOut(clamp((p-0.5)*2))
        case (.bloom,false):
            for i in 0..<count { let l=stagger(clamp(p/0.75),count-1-i,n,0.45),e=easeIn(l);m.petals[i]=PetalMotion(scale:1-0.6*e,rotation:-22*e,alpha:1-l) }
            let l=clamp((p-0.45)/0.55),e=easeIn(l);m.discScale=1-0.5*e;m.discAlpha=1-l;m.baseScale=1-0.55*e;m.baseAlpha=1-l;m.barAlpha=1-clamp(p*2)
        case (.spin,true):
            let s=spring(p,0.74);m.rotation = -120*(1-s);m.scale=0.55+0.45*s;m.alpha=clamp(p*3)
            for i in 0..<count { m.petals[i].alpha=clamp(stagger(p,i,n,0.4)*2.5) }
            m.barAlpha=easeOut(clamp((p-0.4)/0.6))
        case (.spin,false):
            let e=easeIn(p);m.rotation=100*e;m.scale=1-0.5*e;m.alpha=1-pow(p,1.6);m.barAlpha=1-clamp(p*2)
        case (.jelly,true):
            let wobble=exp(-p*5)*sin(p*24)*0.09;m.scale=spring(p,0.42);m.stretchX=1+wobble;m.stretchY=1-wobble;m.alpha=clamp(p*5)
            for i in 0..<count { m.petals[i].offset = -18*(1-spring(stagger(p,i,n,0.25),0.5)) }
            m.barAlpha=easeOut(clamp((p-0.35)/0.65))
        case (.jelly,false):
            let e=easeOut(p);m.scale=1+0.16*e;m.alpha=1-p;m.blur=10*p;m.barAlpha=1-clamp(p*2)
            for i in 0..<count { m.petals[i].offset=18*e }
        case (.snap,true):
            let fade=stage(CGFloat(snapAssemble),CGFloat(snapCrossfade));m.alpha=fade;m.barAlpha=fade;m.dustAlpha=1-fade
        case (.snap,false):
            m.alpha=0;m.barAlpha=1-clamp(p*4);m.dustAlpha=1
        }
        return m
    }
    /// Mode change inside an open wheel: petals rise in again, staggered.
    static func modeSwitch(progress:Double,count:Int)->WheelMotion {
        var m=rest(count);let p=CGFloat(clamp(progress))
        for i in 0..<count { let e=easeOut(stagger(p,i,max(1,count),0.35));m.petals[i].alpha=e;m.petals[i].offset = -8*(1-e) }
        return m
    }

    static func clamp<T:BinaryFloatingPoint>(_ v:T,_ a:T=0,_ b:T=1)->T { min(b,max(a,v)) }
    static func lerp(_ a:CGFloat,_ b:CGFloat,_ t:CGFloat)->CGFloat { a+(b-a)*t }
    static func easeOut(_ p:CGFloat)->CGFloat { 1-pow(1-p,3) }
    static func easeIn(_ p:CGFloat)->CGFloat { p*p*p }
    static func easeInOut(_ p:CGFloat)->CGFloat { p<0.5 ? 4*p*p*p:1-pow(-2*p+2,3)/2 }
    /// Damped spring that settles exactly at p = 1. Lower damping overshoots more (Apple UI springs ≈ 0.75–0.85).
    static func spring(_ p:CGFloat,_ damping:CGFloat=0.78)->CGFloat {
        if p<=0 { return 0 };if p>=1 { return 1 }
        let w=6.5/damping,wd=w*sqrt(1-damping*damping)
        return 1-exp(-damping*w*p)*(cos(wd*p)+(damping*w/wd)*sin(wd*p))
    }
    static func stagger(_ p:CGFloat,_ i:Int,_ n:Int,_ k:CGFloat)->CGFloat { if p>=1 { return 1 };let w=1/(1+CGFloat(n-1)*k);return clamp((p-CGFloat(i)*k*w)/w) }
}
