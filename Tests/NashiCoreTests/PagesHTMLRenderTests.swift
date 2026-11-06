import XCTest
import Foundation
import JavaScriptCore
@testable import NashiCore

final class PagesHTMLRenderTests: XCTestCase {
    /// A colour component stored as a huge float in an untrusted Pages file
    /// must be clamped, not converted to Int (which traps and kills the app).
    func testOutOfRangeFontColourIsClampedInsteadOfTrapping() throws {
        for (red, expected) in [(1e30, "color:rgb(255,0,0)"), (3.4e38, "color:rgb(255,0,0)"), (-1e30, "color:rgb(0,0,0)"), (0.5, "color:rgb(127,0,0)")] {
            let pages = try craftPages(["blocks": [["type": "paragraph", "runs": [["text": "Coloured", "character": ["fontColor": ["r": red, "g": 0, "b": 0]]]]]]])
            let html = try String(contentsOf: PagesCodec.materializeHTML(pages, in: OutputFiles.temporaryDirectory(), token: .init()), encoding: .utf8)
            let span = try XCTUnwrap(html.range(of: ">Coloured</span>").map { html[..<$0.lowerBound] })
            XCTAssertTrue(span.hasSuffix("\(expected);\""), "r=\(red)")
        }
    }

    /// Writes a .pages file from a crafted model through the bundled JS writer.
    private func craftPages(_ model: [String: Any]) throws -> URL {
        let context = try XCTUnwrap(JSContext())
        var error: String?
        context.exceptionHandler = { _, exception in error = exception?.toString() }
        let base64Encode: @convention(block) (String) -> String = { Data($0.utf16.map { UInt8(truncatingIfNeeded: $0) }).base64EncodedString() }
        let base64Decode: @convention(block) (String) -> String = { String(String.UnicodeScalarView((Data(base64Encoded: $0) ?? Data()).map { UnicodeScalar(UInt32($0))! })) }
        let utf8Encode: @convention(block) (String) -> String = { Data($0.utf8).base64EncodedString() }
        let utf8Decode: @convention(block) (String) -> String = { String(decoding: Data(base64Encoded: $0) ?? Data(), as: UTF8.self) }
        let random: @convention(block) (Int) -> String = { Data((0..<$0).map { _ in UInt8.random(in: 0...255) }).base64EncodedString() }
        for (name, block) in [("_nashiBase64Encode", base64Encode as AnyObject), ("_nashiBase64Decode", base64Decode as AnyObject), ("_nashiUTF8Encode", utf8Encode as AnyObject), ("_nashiUTF8Decode", utf8Decode as AnyObject), ("_nashiRandom", random as AnyObject)] { context.setObject(block, forKeyedSubscript: name as NSString) }
        let script = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources/PagesInterop.js")
        context.evaluateScript(try String(contentsOf: script, encoding: .utf8))
        let json = String(decoding: try JSONSerialization.data(withJSONObject: model), as: UTF8.self)
        let encoded = context.objectForKeyedSubscript("NashiPages")?.objectForKeyedSubscript("write")?.call(withArguments: [json])?.toString()
        if let error { throw NashiError.failed(error) }
        let output = try OutputFiles.temporaryDirectory().appendingPathComponent("crafted.pages")
        try XCTUnwrap(encoded.flatMap { Data(base64Encoded: $0) }).write(to: output)
        return output
    }
}
