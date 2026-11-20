import XCTest
@testable import NashiCore

final class ImageAnnotationHistoryTests: XCTestCase {
    private func mark(_ x: Double) -> ImageAnnotation { .init(kind: .rectangle, rect: .init(x:x,y:0,width:10,height:10)) }
    func testMoveUndoRestoresPositionRatherThanDeletingAnotherMark() throws {
        var history=ImageAnnotationHistory(), current=[mark(0)]
        let before=current;current.append(mark(20));history.record(before:before,after:current)
        history.beginInteraction(current)
        for x in [1.0,2,3] { current[0].rect.x=x }
        history.endInteraction(current)
        current=try XCTUnwrap(history.undo(current));XCTAssertEqual(current,[mark(0),mark(20)])
        current=try XCTUnwrap(history.redo(current));XCTAssertEqual(current,[mark(3),mark(20)])
        current=try XCTUnwrap(history.undo(current));current=try XCTUnwrap(history.undo(current))
        XCTAssertEqual(current,[mark(0)])
    }
    func testClearDeleteAndNewEditAfterUndo() throws {
        var history=ImageAnnotationHistory(),current=[mark(0),mark(20)]
        history.record(before:current,after:[]);current=[]
        current=try XCTUnwrap(history.undo(current));XCTAssertEqual(current.count,2)
        let before=current;current.remove(at:0);history.record(before:before,after:current)
        XCTAssertFalse(history.canRedo)
        current=try XCTUnwrap(history.undo(current));XCTAssertEqual(current,[mark(0),mark(20)])
        current=try XCTUnwrap(history.redo(current));XCTAssertEqual(current,[mark(20)])
    }
    func testNoOpGestureRetainsRedoAndResetDropsAllHistory() throws {
        var history=ImageAnnotationHistory();history.record(before:[],after:[mark(0)])
        _=history.undo([mark(0)]);history.beginInteraction([]);XCTAssertFalse(history.canRedo);history.endInteraction([])
        XCTAssertTrue(history.canRedo)
        history.reset();XCTAssertFalse(history.canUndo);XCTAssertFalse(history.canRedo)
    }
    func testHistoryIsBounded() throws {
        var history=ImageAnnotationHistory(limit:2),current:[ImageAnnotation]=[]
        for x in 0..<4 { let before=current;current=[mark(Double(x))];history.record(before:before,after:current) }
        current=try XCTUnwrap(history.undo(current));current=try XCTUnwrap(history.undo(current))
        XCTAssertEqual(current,[mark(1)]);XCTAssertNil(history.undo(current))
    }
}
