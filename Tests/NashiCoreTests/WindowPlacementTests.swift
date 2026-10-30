import XCTest
@testable import NashiCore

final class WindowPlacementTests: XCTestCase {
    func testMinimumWindowWidthKeepsSelectedEdgeAndFitsTheDisplay() {
        let area=CGRect(x:-1920,y:30,width:1920,height:960),minimum=CGSize(width:1000,height:200)
        let left=WindowPlacement.leftThird.frame(in:area,minimumSize:minimum)
        let center=WindowPlacement.middleThird.frame(in:area,minimumSize:minimum)
        let right=WindowPlacement.rightThird.frame(in:area,minimumSize:minimum)
        XCTAssertEqual(left.width,1000);XCTAssertEqual(left.minX,area.minX)
        XCTAssertEqual(center.width,1000);XCTAssertEqual(center.midX,area.midX)
        XCTAssertEqual(right.width,1000);XCTAssertEqual(right.maxX,area.maxX)
        XCTAssertEqual(WindowPlacement.rightHalf.frame(in:area,minimumSize:minimum).maxX,area.maxX)
        XCTAssertEqual(WindowPlacement.leftThird.frame(in:area,minimumSize:CGSize(width:4000,height:200)),area)
        let tall=CGSize(width:100,height:700)
        let top=WindowPlacement.topHalf.frame(in:area,minimumSize:tall),bottom=WindowPlacement.bottomHalf.frame(in:area,minimumSize:tall)
        XCTAssertEqual(top.height,700);XCTAssertEqual(top.maxY,area.maxY)
        XCTAssertEqual(bottom.height,700);XCTAssertEqual(bottom.minY,area.minY)
    }
    func testMaximizeFillsTheExactUsableAreaAcrossDisplaysAndMinimumWidths() {
        for area in [CGRect(x:0,y:30,width:1511,height:910),
                     CGRect(x:-1920,y:-1030,width:1920,height:1006),
                     CGRect(x:1512.25,y:40.5,width:1727.25,height:1090.5)] {
            for scale:CGFloat in [1,2] {
                for minimum:CGFloat in [0,1000,4000] {
                    XCTAssertEqual(WindowPlacement.maximize.frame(in:area,scale:scale,
                        minimumSize:CGSize(width:minimum,height:200)),area)
                }
            }
        }
        XCTAssertEqual(WindowPlacement.maximize.frame(in:.zero),.zero)
        XCTAssertEqual(WindowPlacement.maximize.frame(in:CGRect(x:0,y:0,width:100,height:CGFloat.infinity)),.zero)
    }
    func testRequestedPlacementsPartitionOddAndRetinaScreens() {
        XCTAssertEqual(WindowPlacement.allCases.count, 10)
        for area in [CGRect(x:0,y:24,width:1511,height:910),
                     CGRect(x:-1920,y:-1030,width:1920,height:1006),
                     CGRect(x:1512,y:40,width:1727.5,height:1090)] {
            for scale:CGFloat in [1,2] {
                let left=WindowPlacement.leftThird.frame(in:area,scale:scale)
                let middle=WindowPlacement.middleThird.frame(in:area,scale:scale)
                let right=WindowPlacement.rightThird.frame(in:area,scale:scale)
                XCTAssertEqual(left.minX,area.minX)
                XCTAssertEqual(left.maxX,middle.minX)
                XCTAssertEqual(middle.maxX,right.minX)
                XCTAssertEqual(right.maxX,area.maxX,accuracy:0.5/scale)
                XCTAssertEqual(WindowPlacement.leftTwoThirds.frame(in:area,scale:scale).maxX,middle.maxX)
                XCTAssertEqual(WindowPlacement.rightTwoThirds.frame(in:area,scale:scale).minX,middle.minX)
                XCTAssertEqual(WindowPlacement.leftHalf.frame(in:area,scale:scale).maxX,
                               WindowPlacement.rightHalf.frame(in:area,scale:scale).minX)
                let top=WindowPlacement.topHalf.frame(in:area,scale:scale)
                let bottom=WindowPlacement.bottomHalf.frame(in:area,scale:scale)
                XCTAssertEqual(top.maxY,area.maxY);XCTAssertEqual(bottom.minY,area.minY)
                XCTAssertEqual(bottom.maxY,top.minY)
                XCTAssertEqual(top.height+bottom.height,area.height,accuracy:0.5/scale)
                for frame in [top,bottom] { XCTAssertEqual(frame.minX,area.minX);XCTAssertEqual(frame.width,area.width) }
                for placement in WindowPlacement.allCases where placement.verticalRange == 0...1 {
                    let frame=placement.frame(in:area,scale:scale)
                    XCTAssertEqual(frame.minY,area.minY)
                    XCTAssertEqual(frame.height,area.height)
                    XCTAssertGreaterThan(frame.width,0)
                }
            }
        }
    }

    func testFixedSizeWindowsSitAtTheLayoutAnchor() {
        let area=CGRect(x:0,y:30,width:1800,height:960),size=CGSize(width:400,height:300)
        XCTAssertEqual(WindowPlacement.leftHalf.frame(in:area,fixedSize:size),CGRect(x:0,y:690,width:400,height:300))
        XCTAssertEqual(WindowPlacement.rightHalf.frame(in:area,fixedSize:size),CGRect(x:1400,y:690,width:400,height:300))
        XCTAssertEqual(WindowPlacement.middleThird.frame(in:area,fixedSize:size).midX,area.midX)
        XCTAssertEqual(WindowPlacement.bottomHalf.frame(in:area,fixedSize:size),CGRect(x:0,y:30,width:400,height:300))
        XCTAssertEqual(WindowPlacement.maximize.frame(in:area,fixedSize:CGSize(width:4000,height:4000)),area)
    }
    func testRepeatedLeftAndRightLayoutsHopToTheNeighbouringDisplay() {
        // Laptop in the middle, a display on each side and one stacked above.
        let left=CGRect(x:-1920,y:0,width:1920,height:1050),laptop=CGRect(x:0,y:0,width:1512,height:920)
        let right=CGRect(x:1512,y:-100,width:2560,height:1400),above=CGRect(x:0,y:920,width:1512,height:900)
        let displays=[laptop,right,left,above]
        let rightHalf=WindowPlacement.rightHalf.frame(in:laptop)
        // Not yet in place: arrange on the current display.
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:CGRect(x:10,y:10,width:500,height:400),displays:displays,current:0)==(.rightHalf,0))
        // Right half again → the right display's left half, then that display's right half.
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:rightHalf,displays:displays,current:0)==(.leftHalf,1))
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:WindowPlacement.leftHalf.frame(in:right),displays:displays,current:1)==(.rightHalf,1))
        // The rightmost display has nowhere further to go.
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:WindowPlacement.rightHalf.frame(in:right),displays:displays,current:1)==(.rightHalf,1))
        XCTAssertTrue(WindowPlacement.destination(for:.leftThird,window:WindowPlacement.leftThird.frame(in:laptop),displays:displays,current:0)==(.rightThird,2))
        XCTAssertTrue(WindowPlacement.destination(for:.leftTwoThirds,window:WindowPlacement.leftTwoThirds.frame(in:right),displays:displays,current:1)==(.rightTwoThirds,0))
        // A minimum width keeps the hop working.
        let wide=WindowPlacement.rightHalf.frame(in:laptop,minimumSize:CGSize(width:1000,height:0))
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:wide,displays:displays,current:0)==(.leftHalf,1))
        // A fixed-size window counts as placed once it sits at the anchor.
        let fixed=WindowPlacement.rightHalf.frame(in:laptop,fixedSize:CGSize(width:400,height:300))
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:fixed,displays:displays,current:0)==(.rightHalf,0))
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:fixed,displays:displays,current:0,resizable:false)==(.leftHalf,1))
        // Centered and vertical layouts never leave the display.
        for placement in [WindowPlacement.middleThird,.topHalf,.bottomHalf,.maximize] {
            XCTAssertTrue(WindowPlacement.destination(for:placement,window:placement.frame(in:laptop),displays:displays,current:0)==(placement,0))
        }
        XCTAssertTrue(WindowPlacement.destination(for:.rightHalf,window:rightHalf,displays:[laptop],current:0)==(.rightHalf,0))
    }

    func testContentDragsAndFreshFilePayloadNeverActivateWindowWheel() {
        var intent=WindowDragIntent()
        XCTAssertFalse(intent.accepts(pointerDistance:200,frameChanged:false,freshFileDrag:false,requested:true))
        XCTAssertFalse(intent.accepts(pointerDistance:2,frameChanged:true,freshFileDrag:false,requested:true))
        XCTAssertFalse(intent.accepts(pointerDistance:80,frameChanged:true,freshFileDrag:false,requested:false))
        XCTAssertTrue(intent.accepts(pointerDistance:80,frameChanged:true,freshFileDrag:false,requested:true))
        XCTAssertFalse(intent.accepts(pointerDistance:80,frameChanged:true,freshFileDrag:true,requested:true))
        XCTAssertFalse(intent.accepts(pointerDistance:80,frameChanged:true,freshFileDrag:false,requested:true))
    }

    func testEscapeCancellationLastsUntilTheNextDrag() {
        var intent=WindowDragIntent();intent.cancel()
        XCTAssertFalse(intent.accepts(pointerDistance:80,frameChanged:true,freshFileDrag:false,requested:true))
        var next=WindowDragIntent()
        XCTAssertTrue(next.accepts(pointerDistance:80,frameChanged:true,freshFileDrag:false,requested:true))
    }

    func testEveryPlacementHasADistinctDefaultShortcutAndHotKeyID() {
        XCTAssertEqual(Set(WindowPlacement.allCases.map(\.defaultShortcut)).count,WindowPlacement.allCases.count)
        XCTAssertEqual(WindowPlacement.leftHalf.defaultShortcut.symbols,"⌥⌘←")
        XCTAssertEqual(WindowPlacement.leftThird.defaultShortcut.symbols,"⌃⌥⌘←")
        XCTAssertEqual(WindowPlacement.rightTwoThirds.defaultShortcut.symbols,"⌥⇧⌘→")
        XCTAssertEqual(WindowPlacement.maximize.defaultShortcut.symbols,"⌥⌘F")
        XCTAssertEqual(WindowPlacement.topHalf.defaultShortcut.symbols,"⌥⌘↑")
        XCTAssertEqual(WindowPlacement.bottomHalf.defaultShortcut.symbols,"⌥⌘↓")
        XCTAssertEqual(WindowPlacement.middleThird.defaultShortcut.symbols,"⌃⌥⌘↓")
        for placement in WindowPlacement.allCases {
            XCTAssertNotEqual(placement.hotKeyID,1)
            XCTAssertEqual(WindowPlacement(hotKeyID:placement.hotKeyID),placement)
        }
        XCTAssertNil(WindowPlacement(hotKeyID:1))
        XCTAssertNil(WindowPlacement(hotKeyID:10+UInt32(WindowPlacement.allCases.count)))
    }
    func testRecordedShortcutsPersistClearAndRejectConflicts() {
        let suite="WindowShortcutTests-\(UUID().uuidString)"
        let defaults=UserDefaults(suiteName:suite)!
        addTeardownBlock { defaults.removePersistentDomain(forName:suite) }
        XCTAssertEqual(WindowShortcutPreferences.shortcut(for:.maximize,defaults:defaults)?.symbols,"⌥⌘F")
        let combo=HotKeyCombo(keyCode:38,modifiers:[.control,.option],key:"j")
        XCTAssertEqual(combo.symbols,"⌃⌥J")
        XCTAssertEqual(WindowShortcutPreferences.set(combo,for:.leftThird,defaults:defaults),.saved)
        XCTAssertEqual(WindowShortcutPreferences.shortcut(for:.leftThird,defaults:defaults),combo)
        XCTAssertEqual(WindowShortcutPreferences.set(combo,for:.rightThird,defaults:defaults),.duplicate(WindowPlacement.leftThird.title))
        XCTAssertEqual(WindowShortcutPreferences.set(WindowPlacement.leftHalf.defaultShortcut,for:.rightHalf,defaults:defaults),.duplicate(WindowPlacement.leftHalf.title))
        XCTAssertEqual(WindowShortcutPreferences.set(HotKeyCombo.downloadLink,for:.maximize,defaults:defaults),.duplicate("Download Link…"))
        XCTAssertEqual(WindowShortcutPreferences.set(HotKeyCombo(keyCode:38,modifiers:[.shift],key:"j"),for:.maximize,defaults:defaults),.invalid)
        XCTAssertEqual(WindowShortcutPreferences.set(HotKeyCombo(keyCode:38,modifiers:[],key:"j"),for:.maximize,defaults:defaults),.invalid)
        // A cleared shortcut frees its combination for another layout.
        XCTAssertEqual(WindowShortcutPreferences.set(nil,for:.leftHalf,defaults:defaults),.saved)
        XCTAssertNil(WindowShortcutPreferences.shortcut(for:.leftHalf,defaults:defaults))
        XCTAssertEqual(WindowShortcutPreferences.set(WindowPlacement.leftHalf.defaultShortcut,for:.rightHalf,defaults:defaults),.saved)
        XCTAssertEqual(AppLocalization.text("⌃⌥J is already used by Maximize.",language:"tr"),"⌃⌥J zaten Maximize için kullanılıyor.")
        WindowShortcutPreferences.reset(defaults:defaults)
        for placement in WindowPlacement.allCases {
            XCTAssertEqual(WindowShortcutPreferences.shortcut(for:placement,defaults:defaults),placement.defaultShortcut)
        }
    }
}
