import XCTest
@testable import NashiCore

final class TerminationRequestTests:XCTestCase {
    func testRequestWhileIdleTerminatesAtOnce() {
        var request=TerminationRequest();let token=CancellationToken()
        XCTAssertFalse(request.canTerminate(working:false),"Nothing asked Nashi to quit yet")
        XCTAssertTrue(request.request(working:false) { token.cancel() })
        XCTAssertFalse(token.isCancelled,"There is no job to cancel while idle")
        XCTAssertTrue(request.canTerminate(working:false))
    }
    func testRequestWhileWorkingCancelsTheJobAndTerminatesWhenItStops() {
        var request=TerminationRequest();let token=CancellationToken()
        XCTAssertFalse(request.request(working:true) { token.cancel() },"The job must stop its tools and clean up first")
        XCTAssertTrue(token.isCancelled)
        XCTAssertFalse(request.canTerminate(working:true))
        XCTAssertTrue(request.canTerminate(working:false),"The request must survive until the job has stopped")
    }
    func testNewWorkIsRefusedOnceTerminationWasRequested() {
        var request=TerminationRequest()
        XCTAssertTrue(request.allowsNewWork)
        _=request.request(working:true) {}
        XCTAssertFalse(request.allowsNewWork,"A job started now would keep Nashi running after SIGTERM")
        XCTAssertFalse(request.canTerminate(working:true))
        // The request is still pending after the work stopped: a job slipping in here would have been refused.
        XCTAssertTrue(request.canTerminate(working:false));XCTAssertFalse(request.allowsNewWork)
    }
    func testRepeatedRequestCancelsAgainAndStaysPending() {
        var request=TerminationRequest();var cancels=0
        XCTAssertFalse(request.request(working:true) { cancels+=1 })
        XCTAssertFalse(request.request(working:true) { cancels+=1 })
        XCTAssertEqual(cancels,2);XCTAssertTrue(request.canTerminate(working:false))
    }
}
