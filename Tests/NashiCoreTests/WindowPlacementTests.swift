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
        XCTAssertEqual(WindowPlacement.allCases.count, 8)
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
                for placement in WindowPlacement.allCases {
                    let frame=placement.frame(in:area,scale:scale)
                    XCTAssertEqual(frame.minY,area.minY)
                    XCTAssertEqual(frame.height,area.height)
                    XCTAssertGreaterThan(frame.width,0)
                }
            }
        }
    }

    func testRightAnchoredPlacementsEndOnTheExactFractionalUsableEdge() {
        for (area,scale) in [(CGRect(x:1512,y:40,width:1727.5,height:1090),CGFloat(1)),
                             (CGRect(x:0,y:24,width:1511.4,height:910),CGFloat(1)),
                             (CGRect(x:-1920.5,y:-1030,width:1919.75,height:1006),CGFloat(2))] {
            for placement in [WindowPlacement.rightThird,.rightHalf,.rightTwoThirds] {
                XCTAssertEqual(placement.frame(in:area,scale:scale).maxX,area.maxX,"\(placement) \(area) @\(scale)x")
            }
        }
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
}
