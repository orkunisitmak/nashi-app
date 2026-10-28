import XCTest
import CoreGraphics
import ImageIO
@testable import NashiCore

final class SolidRedactionCoverageTests: XCTestCase {
    private func image() throws -> CGImage {
        let context = try XCTUnwrap(CGContext(data: nil, width: 64, height: 48,
            bitsPerComponent: 8, bytesPerRow: 0, space: CGColorSpace(name: CGColorSpace.sRGB)!,
            bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 1, green: 1, blue: 1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 64, height: 48))
        return try XCTUnwrap(context.makeImage())
    }

    private func rgba(_ image: CGImage) throws -> [UInt8] {
        var bytes = [UInt8](repeating: 0, count: image.width * image.height * 4)
        try bytes.withUnsafeMutableBytes { data in
            let context = try XCTUnwrap(CGContext(data: data.baseAddress, width: image.width, height: image.height,
                bitsPerComponent: 8, bytesPerRow: image.width * 4,
                space: CGColorSpace(name: CGColorSpace.sRGB)!,
                bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue | CGBitmapInfo.byteOrder32Big.rawValue))
            context.draw(image, in: CGRect(x: 0, y: 0, width: image.width, height: image.height))
        }
        return bytes
    }

    func testSolidImagePreviewAndExportAlwaysUseOpaqueSelectedRGB() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = directory.appendingPathComponent("Source.png")
        try ImageEngine.write(image(), to: input)
        let original = try Data(contentsOf: input)
        for color in ["#20406000", "#20406080", "#204060FF", "#204060"] {
            var settings = ImageRedactionOptions(); settings.style = .solid; settings.color = color
            var options = OperationOptions(); options.imageRedaction = settings
            options.redactions = [.init(x: 16, y: 12, width: 32, height: 24)]
            let preview = try ImageEngine.preview(image(), tool: .redactImage, options: options)
            let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .tool(.redactImage), options: options)).first)
            let source = try XCTUnwrap(CGImageSourceCreateWithURL(output as CFURL, nil))
            let exported = try XCTUnwrap(CGImageSourceCreateImageAtIndex(source, 0, nil))
            for rendered in [preview, exported] {
                let pixels = try rgba(rendered), center = (24 * 64 + 32) * 4
                XCTAssertEqual(Array(pixels[center..<center + 4]), [32, 64, 96, 255], color)
                XCTAssertEqual(Array(pixels[0..<4]), [255, 255, 255, 255], "Unselected pixels stay unchanged.")
            }
        }
        XCTAssertEqual(try Data(contentsOf: input), original)
    }

    private func video(_ directory: URL) throws -> URL {
        let input = directory.appendingPathComponent("Source.mp4")
        try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-nostdin", "-n",
            "-f", "lavfi", "-i", "color=c=white:size=64x48:rate=8:duration=1",
            "-c:v", "libx264", "-pix_fmt", "yuv420p", input.path])
        return input
    }

    private func grayFrame(_ input: URL, time: String) throws -> [UInt8] {
        Array(try ProcessRunner.run(Dependencies.executable("ffmpeg"), ["-v", "error", "-nostdin",
            "-ss", time, "-i", input.path, "-map", "0:v:0", "-frames:v", "1",
            "-pix_fmt", "gray", "-f", "rawvideo", "-"]).output)
    }

    func testSolidVideoIgnoresColorOpacityAndKeepsTemporalBounds() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = try video(directory), original = try Data(contentsOf: input)
        for color in ["#00000000", "#00000080", "#000000FF", "#000000"] {
            var options = OperationOptions(); options.redactionMode = "solid"; options.backgroundColor = color
            options.videoRedactions = [.init(rect: .init(x: 16, y: 12, width: 32, height: 24), start: 0.25, end: 0.75)]
            let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .tool(.redactVideo), options: options)).first)
            let active = try grayFrame(output, time: "0.5"), inactive = try grayFrame(output, time: "0")
            XCTAssertEqual(active.count, 64 * 48); XCTAssertEqual(inactive.count, 64 * 48)
            guard active.count == 64 * 48, inactive.count == 64 * 48 else { continue }
            XCTAssertLessThan(active[24 * 64 + 32], 10, color)
            XCTAssertGreaterThan(active[0], 240, "Unselected pixels stay visible.")
            XCTAssertGreaterThan(inactive[24 * 64 + 32], 240, "The time range must still apply.")
        }
        XCTAssertEqual(try Data(contentsOf: input), original)
    }

    func testSolidVideoKeepsSelectedRGBInsteadOfAlwaysUsingBlack() throws {
        let directory=try OutputFiles.temporaryDirectory()
        defer{try? FileManager.default.removeItem(at:directory)}
        let input=try video(directory),original=try Data(contentsOf:input)
        var options=OperationOptions();options.redactionMode="solid";options.backgroundColor="#FF000080"
        options.videoRedactions=[.init(rect:.init(x:16,y:12,width:32,height:24),start:0.25,end:0.75)]
        let output=try XCTUnwrap(ConversionEngine.run(.init(inputs:[input],operation:.tool(.redactVideo),options:options)).first)
        func frame(_ time:String)throws->[UInt8] {
            Array(try ProcessRunner.run(Dependencies.executable("ffmpeg"),["-v","error","-nostdin","-ss",time,"-i",output.path,
                "-map","0:v:0","-frames:v","1","-pix_fmt","rgb24","-f","rawvideo","-"]).output)
        }
        let active=try frame("0.5"),inactive=try frame("0")
        XCTAssertEqual(active.count,64*48*3);XCTAssertEqual(inactive.count,64*48*3)
        guard active.count==64*48*3,inactive.count==64*48*3 else{return}
        let center=(24*64+32)*3
        XCTAssertGreaterThan(active[center],200);XCTAssertLessThan(active[center+1],40);XCTAssertLessThan(active[center+2],40)
        XCTAssertTrue(active[0..<3].allSatisfy{$0>240});XCTAssertTrue(inactive[center..<center+3].allSatisfy{$0>240})
        XCTAssertEqual(try Data(contentsOf:input),original)
    }

    func testSolidVideoCoversEveryPixelTouchedByFractionalSelection() throws {
        let directory = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: directory) }
        let input = try video(directory)
        var options = OperationOptions(); options.redactionMode = "solid"; options.backgroundColor = "#000000"
        // Both the origin and size have fractional parts; the selected right
        // and bottom edges cross into the final pixel row and column.
        options.redactions = [.init(x: 16.75, y: 12.75, width: 30.5, height: 22.5)]
        let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .tool(.redactVideo), options: options)).first)
        let pixels = try grayFrame(output, time: "0.5")
        XCTAssertEqual(pixels.count, 64 * 48)
        guard pixels.count == 64 * 48 else { return }
        let selected = (12..<36).flatMap { y in (16..<48).map { x in pixels[y * 64 + x] } }
        XCTAssertLessThan(try XCTUnwrap(selected.max()), 20, "The entire selected area must be covered.")
        XCTAssertGreaterThan(pixels[0], 240)
        XCTAssertGreaterThan(pixels[44 * 64 + 60], 240)
    }
}
