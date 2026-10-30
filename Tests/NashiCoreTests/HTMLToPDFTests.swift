import XCTest
import AppKit
import PDFKit
@testable import NashiCore

final class HTMLToPDFTests: XCTestCase {
    private var root: URL!
    private var site: URL!
    override func setUpWithError() throws {
        root = try OutputFiles.temporaryDirectory().resolvingSymlinksInPath()
        site = root.appendingPathComponent("Site", isDirectory: true)
        try FileManager.default.createDirectory(at: site.appendingPathComponent("assets", isDirectory: true), withIntermediateDirectories: true)
    }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    func testHTMLAndHTMInputsOfferPDF() {
        XCTAssertEqual(FeatureCatalog.formats(for: [URL(fileURLWithPath: "/tmp/page.html")]), ["pdf"])
        XCTAssertEqual(FeatureCatalog.formats(for: [URL(fileURLWithPath: "/tmp/page.HTM"), URL(fileURLWithPath: "/tmp/other.html")]), ["pdf"])
        XCTAssertEqual(FeatureCatalog.tools(for: [URL(fileURLWithPath: "/tmp/page.html")]), [])
    }

    func testHTMLPrintsBrowserLayoutScriptDrawnCodesLocalAssetsAndUndeclaredUTF8() throws {
        try Self.png(255, 128, 0).write(to: site.appendingPathComponent("assets/orange.png"))
        try "header { display: flex; justify-content: space-between } .box { width: 80px; height: 80px; background: #00C8C8 }".write(to: site.appendingPathComponent("site.css"), atomically: true, encoding: .utf8)
        // Without a charset declaration, WebKit alone would read these bytes as Windows-1252.
        let input = site.appendingPathComponent("Fatura.html")
        try Data("""
        <!doctype html><html><head><title>Fatura Şişli</title><link rel="stylesheet" href="site.css"><script>document.write('Script ran')</script></head>
        <body style="margin:32px"><header><span>Left column</span><span>Right column</span></header><p>Merhaba Şişli ğüşıöç İ</p>
        <img src="assets/orange.png"><div class="box"></div><table style="width:100%;border-collapse:collapse"><tr><td style="border:1px solid">Ürün</td><td style="border:1px solid">Adet</td></tr></table>
        <div id="code"></div><script>
        // Like the QR code libraries in e-invoices: draw on a canvas, then swap in an image after an asynchronous check.
        const canvas = document.createElement('canvas'); canvas.width = canvas.height = 80;
        const context = canvas.getContext('2d'); context.fillStyle = '#00A000'; context.fillRect(0, 0, 80, 80);
        const image = new Image(); image.style.display = 'none'; document.getElementById('code').append(canvas, image);
        const check = new Image(); check.onload = () => { image.src = canvas.toDataURL('image/png'); image.style.display = 'block'; canvas.style.display = 'none' };
        check.src = 'data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7';
        </script>
        <p><a href="next.html">Next page</a> <a href="https://example.com/terms">Terms</a></p><p style="page-break-before:always">Second page</p></body></html>
        """.utf8).write(to: input)
        let original = try Data(contentsOf: input)
        let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .convert("pdf"))).first)
        XCTAssertEqual(output, site.appendingPathComponent("Fatura.pdf"))
        let pdf = try XCTUnwrap(PDFDocument(url: output))
        XCTAssertEqual(pdf.pageCount, 2)
        let text = try XCTUnwrap(pdf.string)
        XCTAssertTrue(text.contains("Merhaba Şişli ğüşıöç İ"), text)
        XCTAssertTrue(text.contains("Ürün"))
        XCTAssertTrue(text.contains("Second page"))
        XCTAssertTrue(text.contains("Script ran"))
        XCTAssertEqual(pdf.documentAttributes?[PDFDocumentAttribute.titleAttribute] as? String, "Fatura Şişli")
        // Flex layout keeps both labels on one line, at opposite page edges.
        let page = try XCTUnwrap(pdf.page(at: 0)), width = page.bounds(for: .mediaBox).width
        let left = try XCTUnwrap(pdf.findString("Left column", withOptions: []).first).bounds(for: page)
        let right = try XCTUnwrap(pdf.findString("Right column", withOptions: []).first).bounds(for: page)
        XCTAssertLessThan(left.minX, width / 4)
        XCTAssertGreaterThan(right.maxX, width * 3 / 4)
        XCTAssertEqual(left.midY, right.midY, accuracy: 2)
        XCTAssertGreaterThan(try Self.pixels(near: (255, 128, 0), on: page), 1000)
        XCTAssertGreaterThan(try Self.pixels(near: (0, 200, 200), on: page), 1000)
        XCTAssertGreaterThan(try Self.pixels(near: (0, 160, 0), on: page), 1000, "The script-drawn code is missing.")
        // A link to a file beside the page cannot open from a shared PDF; web links stay.
        XCTAssertEqual(page.annotations.compactMap { $0.url?.absoluteString }, ["https://example.com/terms"])
        XCTAssertEqual(try Data(contentsOf: input), original)
    }

    func testHTMLReadsOnlyItsFolderAndNeverContactsTheNetwork() throws {
        let probe = try ConnectionProbe()
        defer { probe.close() }
        let remote = "http://127.0.0.1:\(probe.port)"
        // Control traffic proves the probe sees loopback TCP and UDP. URLSession may retry its request.
        URLSession.shared.dataTask(with: URL(string: remote + "/control")!).resume()
        probe.sendDatagram()
        XCTAssertTrue(wait { probe.connections > 0 && probe.datagrams > 0 })
        _ = wait(seconds: 0.5) { false }
        let control = (probe.connections, probe.datagrams)
        let outside = root.appendingPathComponent("outside.png")
        try Self.png(255, 0, 255).write(to: outside)
        try FileManager.default.createSymbolicLink(at: site.appendingPathComponent("assets/linked.png"), withDestinationURL: outside)
        try "Sibling secret marker".write(to: site.appendingPathComponent("secret.txt"), atomically: true, encoding: .utf8)
        try "@import url('\(remote)/nested.css'); p { background-image: url('\(remote)/nested.png') }".write(to: site.appendingPathComponent("local.css"), atomically: true, encoding: .utf8)
        let input = site.appendingPathComponent("Probe.html")
        try """
        <!doctype html><html><head><meta charset="utf-8"><meta http-equiv="refresh" content="0;url=\(remote)/refresh">
        <link rel="stylesheet" href="\(remote)/style.css"><link rel="stylesheet" href="local.css"><link rel="preconnect" href="\(remote)"><link rel="prefetch" href="\(remote)/prefetch">
        <style>@import url('\(remote)/import.css'); @font-face { font-family: Remote; src: url('\(remote)/font.woff2') } body { font-family: Remote; background: url('\(remote)/background.png') }</style></head>
        <body><p>Offline probe</p><img src="\(remote)/image.png"><img srcset="\(remote)/srcset.png 1x"><img loading="lazy" src="\(remote)/lazy.png">
        <video src="\(remote)/video.mp4" poster="\(remote)/poster.png" preload="auto" autoplay></video><iframe src="\(remote)/frame"></iframe><object data="\(remote)/object"></object>
        <svg width="20" height="20"><image href="\(remote)/svg.png" width="20" height="20"/></svg>
        <img src="../outside.png"><img src="\(outside.absoluteString)"><img src="assets/linked.png"><iframe src="../outside.png"></iframe>
        <pre id="report"></pre><script>
        const report = line => { document.getElementById('report').textContent += line + '\\n' };
        const frame = document.body.appendChild(document.createElement('iframe'));
        report('peer ' + typeof RTCPeerConnection + ', frame peer ' + typeof frame.contentWindow.RTCPeerConnection + ', camera ' + typeof navigator.mediaDevices + ', location ' + typeof navigator.geolocation);
        fetch('\(remote)/fetch').catch(() => {}); navigator.sendBeacon('\(remote)/beacon', 'x');
        // Files beside the page may be shown by markup, but scripts may not read them into the PDF.
        fetch('secret.txt').then(response => response.text()).then(report, () => {});
        const sibling = new XMLHttpRequest(); sibling.open('GET', 'secret.txt', false); try { sibling.send(); report(sibling.responseText) } catch {}
        const request = new XMLHttpRequest(); request.open('GET', '\(remote)/xhr'); request.send();
        try { new WebSocket('ws://127.0.0.1:\(probe.port)/socket') } catch {}
        try { new EventSource('\(remote)/events') } catch {}
        new Image().src = '\(remote)/script-image'; import('\(remote)/module.js').catch(() => {});
        new Worker(URL.createObjectURL(new Blob(["fetch('\(remote)/worker').catch(() => {})"])));
        window.open('\(remote)/popup'); setTimeout(() => { location.href = '\(remote)/navigate' }, 10);
        const Peer = window.RTCPeerConnection || frame.contentWindow.RTCPeerConnection;
        if (Peer) { const peer = new Peer({ iceServers: [{ urls: 'stun:127.0.0.1:\(probe.udpPort)' }] }); peer.createDataChannel('x'); peer.createOffer().then(offer => peer.setLocalDescription(offer)) }
        </script></body></html>
        """.write(to: input, atomically: true, encoding: .utf8)
        let output = try XCTUnwrap(ConversionEngine.run(.init(inputs: [input], operation: .convert("pdf"))).first)
        _ = wait(seconds: 1) { false }
        XCTAssertEqual(probe.connections, control.0, "The conversion opened a network connection.")
        XCTAssertEqual(probe.datagrams, control.1, "The conversion sent a datagram, as WebRTC does.")
        let pdf = try XCTUnwrap(PDFDocument(url: output))
        let text = try XCTUnwrap(pdf.string)
        XCTAssertTrue(text.contains("Offline probe"))
        XCTAssertTrue(text.contains("peer undefined, frame peer undefined, camera undefined, location undefined"), text)
        XCTAssertFalse(text.contains("Sibling secret marker"), "A page script read a file beside the page.")
        for index in 0..<pdf.pageCount { XCTAssertEqual(try Self.pixels(near: (255, 0, 255), on: XCTUnwrap(pdf.page(at: index))), 0, "A file outside the page's folder was rendered.") }
        XCTAssertNil(try Data(contentsOf: output).range(of: Data("file:".utf8)))
    }

    func testCancellationAndPageLimitsPublishNothing() throws {
        let long = site.appendingPathComponent("Long.html")
        try ("<!doctype html><meta charset=\"utf-8\">" + String(repeating: "<p>" + String(repeating: "Nashi cancellation fixture. ", count: 60) + "</p>", count: 600)).write(to: long, atomically: true, encoding: .utf8)
        let token = CancellationToken()
        DispatchQueue.global().asyncAfter(deadline: .now() + 0.2) { token.cancel() }
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs: [long], operation: .convert("pdf")), token: token)) { error in
            guard case NashiError.cancelled = error else { return XCTFail("\(error)") }
        }
        let tall = site.appendingPathComponent("Tall.html")
        try "<!doctype html><p>Top</p><div style=\"height:10000000px\"></div>".write(to: tall, atomically: true, encoding: .utf8)
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs: [tall], operation: .convert("pdf")))) { error in
            XCTAssertEqual(error.localizedDescription, "This HTML file would need more than 1,000 PDF pages.")
        }
        // A short page can still force more than 1,000 pages with page breaks.
        let breaks = site.appendingPathComponent("Breaks.html")
        try ("<!doctype html>" + String(repeating: "<p style=\"break-before:page\">Page</p><p style=\"page-break-before:always\">Page</p>", count: 520)).write(to: breaks, atomically: true, encoding: .utf8)
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs: [breaks], operation: .convert("pdf")))) { error in
            XCTAssertEqual(error.localizedDescription, "This HTML file would need more than 1,000 PDF pages.")
        }
        _ = wait(seconds: 2) { false }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: site.path).sorted(), ["Breaks.html", "Long.html", "Tall.html", "assets"])
    }

    /// Keeps the main run loop turning, as WebKit and URLSession callbacks need it.
    private func wait(seconds: TimeInterval = 10, until condition: () -> Bool) -> Bool {
        let deadline = Date().addingTimeInterval(seconds)
        while !condition() && Date() < deadline { _ = RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.01)) }
        return condition()
    }

    private static func png(_ red: UInt8, _ green: UInt8, _ blue: UInt8) throws -> Data {
        let bitmap = try XCTUnwrap(NSBitmapImageRep(bitmapDataPlanes: nil, pixelsWide: 80, pixelsHigh: 80, bitsPerSample: 8, samplesPerPixel: 4, hasAlpha: true, isPlanar: false, colorSpaceName: .deviceRGB, bytesPerRow: 0, bitsPerPixel: 0))
        for y in 0..<80 { for x in 0..<80 { bitmap.setColor(NSColor(deviceRed: CGFloat(red) / 255, green: CGFloat(green) / 255, blue: CGFloat(blue) / 255, alpha: 1), atX: x, y: y) } }
        return try XCTUnwrap(bitmap.representation(using: .png, properties: [:]))
    }

    private static func pixels(near color: (Int, Int, Int), on page: PDFPage) throws -> Int {
        let bounds = page.bounds(for: .mediaBox), width = Int(bounds.width), height = Int(bounds.height)
        let context = try XCTUnwrap(CGContext(data: nil, width: width, height: height, bitsPerComponent: 8, bytesPerRow: width * 4, space: CGColorSpaceCreateDeviceRGB(), bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(gray: 1, alpha: 1)); context.fill(CGRect(x: 0, y: 0, width: width, height: height))
        page.draw(with: .mediaBox, to: context)
        let bytes = try XCTUnwrap(context.data).assumingMemoryBound(to: UInt8.self)
        return (0..<(width * height)).filter { abs(Int(bytes[$0 * 4]) - color.0) < 40 && abs(Int(bytes[$0 * 4 + 1]) - color.1) < 40 && abs(Int(bytes[$0 * 4 + 2]) - color.2) < 40 }.count
    }

    /// Counts loopback TCP connections, including ones that never send a request, and UDP datagrams.
    private final class ConnectionProbe: @unchecked Sendable {
        let port: Int, udpPort: Int
        private let listener: Int32, receiver: Int32
        private let lock = NSLock()
        private var accepted = 0, received = 0
        init() throws {
            (listener, port) = try Self.open(SOCK_STREAM)
            (receiver, udpPort) = try Self.open(SOCK_DGRAM)
            Thread.detachNewThread { [self] in
                while true {
                    let client = Darwin.accept(listener, nil, nil)
                    guard client >= 0 else { return }
                    lock.lock(); accepted += 1; lock.unlock()
                    Darwin.close(client)
                }
            }
            Thread.detachNewThread { [self] in
                var buffer = [UInt8](repeating: 0, count: 2048)
                while Darwin.recv(receiver, &buffer, buffer.count, 0) >= 0 { lock.lock(); received += 1; lock.unlock() }
            }
        }
        var connections: Int { lock.lock(); defer { lock.unlock() }; return accepted }
        var datagrams: Int { lock.lock(); defer { lock.unlock() }; return received }
        func sendDatagram() {
            let sender = Darwin.socket(AF_INET, SOCK_DGRAM, 0), bytes = Array("control".utf8)
            defer { Darwin.close(sender) }
            var address = Self.loopback(udpPort)
            _ = withUnsafePointer(to: &address) { $0.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.sendto(sender, bytes, bytes.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size)) } }
        }
        func close() { Darwin.shutdown(listener, SHUT_RDWR); Darwin.close(listener); Darwin.close(receiver) }

        private static func loopback(_ port: Int) -> sockaddr_in {
            var address = sockaddr_in()
            address.sin_len = UInt8(MemoryLayout<sockaddr_in>.size)
            address.sin_family = sa_family_t(AF_INET)
            address.sin_addr.s_addr = inet_addr("127.0.0.1")
            address.sin_port = UInt16(port).bigEndian
            return address
        }
        private static func open(_ type: Int32) throws -> (Int32, Int) {
            let descriptor = Darwin.socket(AF_INET, type, 0)
            var address = loopback(0), length = socklen_t(MemoryLayout<sockaddr_in>.size)
            let ready = withUnsafeMutablePointer(to: &address) { pointer in
                pointer.withMemoryRebound(to: sockaddr.self, capacity: 1) { Darwin.bind(descriptor, $0, length) == 0 && (type != SOCK_STREAM || Darwin.listen(descriptor, 64) == 0) && Darwin.getsockname(descriptor, $0, &length) == 0 }
            }
            guard descriptor >= 0, ready else { throw NashiError.failed("The loopback probe could not start.") }
            return (descriptor, Int(UInt16(bigEndian: address.sin_port)))
        }
    }
}
