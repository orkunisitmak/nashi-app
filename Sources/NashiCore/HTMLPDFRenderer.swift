import AppKit
import PDFKit
import UniformTypeIdentifiers
import WebKit

/// Prints a local HTML file with the system WebKit engine, so the PDF keeps the
/// layout Safari shows. Page scripts run without network or device access, WebKit
/// receives only regular files from the page's own folder, and every other load is
/// blocked before it starts.
enum HTMLPDFRenderer {
    static let scheme = "nashi-html"
    static let tooManyPages = NashiError.invalidInput("This HTML file would need more than 1,000 PDF pages.")

    static func render(_ input: URL, to output: URL, token: CancellationToken) throws {
        try token.check()
        let job = Job(input: input, output: output)
        onMain { job.start() }
        // The app converts on a background thread. The command line and tests
        // call from the main thread, whose run loop must keep turning for WebKit.
        let deadline = Date().addingTimeInterval(120)
        while job.outcome == nil {
            if token.isCancelled || Date() >= deadline {
                let error: NashiError = token.isCancelled ? .cancelled : .failed("The HTML file did not finish rendering. No PDF was exported.")
                onMain { job.finish(.failure(error)) }
                throw error
            }
            if Thread.isMainThread { _ = RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.01)) }
            else { _ = job.signal.wait(timeout: .now() + 0.01) }
        }
        try job.outcome?.get()
    }

    /// WebKit reads an undeclared page as Windows-1252, but such files are nearly always UTF-8.
    static func charset(_ html: Data) -> String? {
        let head = String(decoding: html.prefix(1024), as: UTF8.self)
        guard head.range(of: #"(?i)<meta[^>]*charset"#, options: .regularExpression) == nil, String(data: html, encoding: .utf8) != nil else { return nil }
        return "utf-8"
    }

    private static func onMain(_ work: @escaping @MainActor @Sendable () -> Void) {
        if Thread.isMainThread { MainActor.assumeIsolated(work) } else { DispatchQueue.main.async { MainActor.assumeIsolated(work) } }
    }

    @MainActor private final class Job: NSObject, WKNavigationDelegate, WKUIDelegate, WKURLSchemeHandler {
        // WebRTC reaches the network outside content blockers, and device APIs could prompt or record.
        static let withoutDevices = "for (const name of ['RTCPeerConnection', 'webkitRTCPeerConnection', 'RTCDataChannel', 'RTCIceCandidate', 'RTCSessionDescription', 'speechSynthesis']) delete window[name]; for (const name of ['mediaDevices', 'geolocation', 'getUserMedia', 'webkitGetUserMedia']) delete Navigator.prototype[name]"
        nonisolated let signal = DispatchSemaphore(value: 0)
        nonisolated private let lock = NSLock()
        nonisolated(unsafe) private var result: Result<Void, Error>?
        private let input: URL, output: URL, folder: URL, page: URL
        private var view: WKWebView?, window: NSWindow?, navigation: WKNavigation?, rules: URL?, printing = false
        private lazy var info = Job.printInfo(output)
        // Points available for content once the margins are taken from the paper.
        private var printable: NSSize { NSSize(width: info.paperSize.width - info.leftMargin - info.rightMargin, height: info.paperSize.height - info.topMargin - info.bottomMargin) }

        nonisolated init(input: URL, output: URL) {
            self.input = input; self.output = output
            folder = input.deletingLastPathComponent().resolvingSymlinksInPath().standardizedFileURL
            page = URL(string: HTMLPDFRenderer.scheme + "://page/")!.appendingPathComponent(input.lastPathComponent)
            super.init()
        }
        nonisolated var outcome: Result<Void, Error>? { lock.lock(); defer { lock.unlock() }; return result }

        func start() {
            guard outcome == nil else { return }
            do {
                let directory = try OutputFiles.temporaryDirectory(); rules = directory
                guard let store = WKContentRuleListStore(url: directory) else { throw NashiError.failed("WebKit could not block remote resources. No PDF was exported.") }
                // Content blockers stop remote subresources before WebKit requests them. Scripts
                // also may not fetch files beside the page, so they cannot copy them into the PDF.
                let offline = #"[{"trigger":{"url-filter":".*"},"action":{"type":"block"}},{"trigger":{"url-filter":"^\#(HTMLPDFRenderer.scheme):"},"action":{"type":"ignore-previous-rules"}},{"trigger":{"url-filter":".*","resource-type":["raw"]},"action":{"type":"block"}}]"#
                store.compileContentRuleList(forIdentifier: "Offline", encodedContentRuleList: offline) { list, _ in
                    MainActor.assumeIsolated {
                        guard let list else { return self.finish(.failure(NashiError.failed("WebKit could not block remote resources. No PDF was exported."))) }
                        self.load(blocking: list)
                    }
                }
            } catch { finish(.failure(error)) }
        }

        private func load(blocking list: WKContentRuleList) {
            guard outcome == nil else { return }
            let configuration = WKWebViewConfiguration()
            configuration.websiteDataStore = .nonPersistent()
            // Page scripts run because documents such as e-invoices draw their QR codes with them.
            // They lose WebRTC, camera, microphone and location before the first page script runs.
            configuration.userContentController.addUserScript(WKUserScript(source: Job.withoutDevices, injectionTime: .atDocumentStart, forMainFrameOnly: false))
            configuration.mediaTypesRequiringUserActionForPlayback = .all
            configuration.preferences.isFraudulentWebsiteWarningEnabled = false
            if #available(macOS 13.3, *) { configuration.preferences.shouldPrintBackgrounds = true }
            configuration.userContentController.add(list)
            // Nashi serves the page and its files itself, so WebKit gets no read access to the disk.
            configuration.setURLSchemeHandler(self, forURLScheme: HTMLPDFRenderer.scheme)
            let view = WKWebView(frame: NSRect(origin: .zero, size: printable), configuration: configuration)
            // A dark app appearance must not switch the page to its dark color scheme.
            view.appearance = NSAppearance(named: .aqua)
            view.navigationDelegate = self
            view.uiDelegate = self
            let window = NSWindow(contentRect: view.frame, styleMask: .borderless, backing: .buffered, defer: true)
            window.isReleasedWhenClosed = false
            window.contentView = view
            self.view = view; self.window = window
            navigation = view.load(URLRequest(url: page))
        }

        func webView(_ webView: WKWebView, start task: any WKURLSchemeTask) {
            guard let url = task.request.url, let file = local(url), let data = try? Data(contentsOf: file, options: .mappedIfSafe) else {
                return task.didFailWithError(URLError(.fileDoesNotExist))
            }
            let type = UTType(filenameExtension: file.pathExtension)
            let html = type?.conforms(to: .html) == true
            task.didReceive(URLResponse(url: url, mimeType: html ? "text/html" : type?.preferredMIMEType ?? "application/octet-stream", expectedContentLength: data.count, textEncodingName: html ? HTMLPDFRenderer.charset(data) : nil))
            task.didReceive(data)
            task.didFinish()
        }
        func webView(_ webView: WKWebView, stop task: any WKURLSchemeTask) {}

        /// The chosen page, or a regular file it links to inside its folder after links are resolved.
        private func local(_ url: URL) -> URL? {
            guard url.scheme == HTMLPDFRenderer.scheme else { return nil }
            if url.path == page.path { return input }
            let file = folder.appendingPathComponent(String(url.path.drop { $0 == "/" })).resolvingSymlinksInPath().standardizedFileURL
            guard file.path.hasPrefix(folder.path + "/"),
                  let values = try? file.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey]),
                  values.isRegularFile == true, (values.fileSize ?? .max) <= 50_000_000 else { return nil }
            return file
        }

        func webView(_ webView: WKWebView, requestMediaCapturePermissionFor origin: WKSecurityOrigin, initiatedByFrame frame: WKFrameInfo, type: WKMediaCaptureType, decisionHandler: @escaping @MainActor @Sendable (WKPermissionDecision) -> Void) {
            decisionHandler(.deny)
        }

        func webView(_ webView: WKWebView, decidePolicyFor action: WKNavigationAction, decisionHandler: @escaping @MainActor @Sendable (WKNavigationActionPolicy) -> Void) {
            // Frames may show files beside the page. Nothing navigates away from it.
            guard let url = action.request.url, url.scheme == HTMLPDFRenderer.scheme else { return decisionHandler(.cancel) }
            decisionHandler(action.targetFrame?.isMainFrame == false || url.path == page.path ? .allow : .cancel)
        }

        func webView(_ webView: WKWebView, didFinish navigation: WKNavigation!) {
            guard navigation === self.navigation, outcome == nil else { return }
            // Lazy images and web fonts must be ready. Scripts may swap in an image, such as a QR
            // code, just after loading, so images are checked again after a short pause.
            let ready = "const settled = () => Promise.all([...document.images].map(image => image.complete || new Promise(done => { image.addEventListener('load', done, { once: true }); image.addEventListener('error', done, { once: true }) }))); for (const image of document.images) image.loading = 'eager'; await settled(); await document.fonts.ready; await new Promise(done => setTimeout(done, 100)); await settled(); return document.documentElement.scrollHeight"
            webView.callAsyncJavaScript(ready, arguments: [:], in: nil, in: .defaultClient) { height in
                MainActor.assumeIsolated { self.printPDF(contentHeight: (try? height.get()) as? Double ?? 0) }
            }
        }

        private func printPDF(contentHeight: Double) {
            guard outcome == nil, let view, let window else { return }
            // CSS pixels print at three quarters of a point.
            guard contentHeight * 0.75 / printable.height <= 1000 else { return finish(.failure(HTMLPDFRenderer.tooManyPages)) }
            let operation = view.printOperation(with: info)
            operation.showsPrintPanel = false
            operation.showsProgressPanel = false
            operation.jobTitle = view.title.flatMap { $0.isEmpty ? nil : $0 } ?? input.deletingPathExtension().lastPathComponent
            operation.view?.frame = view.bounds
            // run() keeps paginating a WKWebView without end; a window-modal run does not.
            printing = true
            operation.runModal(for: window, delegate: self, didRun: #selector(printed(_:success:context:)), contextInfo: nil)
        }

        // AppKit finishes a window-modal print on its own thread.
        @objc nonisolated private func printed(_ operation: NSPrintOperation, success: Bool, context: UnsafeMutableRawPointer?) {
            DispatchQueue.main.async { MainActor.assumeIsolated { self.printed(success: success) } }
        }

        private func printed(success: Bool) {
            printing = false
            guard success, let data = try? Data(contentsOf: output), let pdf = PDFDocument(data: data), pdf.pageCount > 0 else {
                return finish(.failure(NashiError.failed("WebKit could not print this HTML file to PDF.")))
            }
            // Forced page breaks add pages that the height estimate cannot see.
            guard pdf.pageCount <= 1000 else { return finish(.failure(HTMLPDFRenderer.tooManyPages)) }
            // Links to files beside the page cannot open from a shared PDF.
            var removed = false
            for index in 0..<pdf.pageCount {
                guard let sheet = pdf.page(at: index) else { continue }
                for link in sheet.annotations where link.url?.scheme == HTMLPDFRenderer.scheme { sheet.removeAnnotation(link); removed = true }
            }
            guard !removed || pdf.write(to: output) else { return finish(.failure(NashiError.failed("WebKit could not print this HTML file to PDF."))) }
            finish(.success(()))
        }

        func webView(_ webView: WKWebView, didFail navigation: WKNavigation!, withError error: Error) { failed(navigation, error) }
        func webView(_ webView: WKWebView, didFailProvisionalNavigation navigation: WKNavigation!, withError error: Error) { failed(navigation, error) }
        func webViewWebContentProcessDidTerminate(_ webView: WKWebView) { finish(.failure(NashiError.failed("WebKit stopped while rendering the HTML file. No PDF was exported."))) }
        private func failed(_ navigation: WKNavigation!, _ error: Error) {
            guard navigation === self.navigation else { return }
            finish(.failure(NashiError.invalidInput("The HTML file could not be opened. \(error.localizedDescription)")))
        }

        func finish(_ value: Result<Void, Error>) {
            // A cancelled job keeps the view until AppKit stops printing from it.
            if !printing {
                view?.stopLoading(); view?.navigationDelegate = nil; view?.uiDelegate = nil
                window?.contentView = nil; window?.close()
                view = nil; window = nil; navigation = nil
                if let rules { try? FileManager.default.removeItem(at: rules) }
                rules = nil
            }
            lock.lock(); if result == nil { result = value }; lock.unlock()
            signal.signal()
        }

        private static func printInfo(_ output: URL) -> NSPrintInfo {
            // The user's default paper size, with the margins browsers print by default.
            let info = NSPrintInfo(dictionary: [.jobDisposition: NSPrintInfo.JobDisposition.save, .jobSavingURL: output])
            info.topMargin = 36; info.bottomMargin = 36; info.leftMargin = 36; info.rightMargin = 36
            info.horizontalPagination = .fit
            info.verticalPagination = .automatic
            info.isHorizontallyCentered = false
            info.isVerticallyCentered = false
            return info
        }
    }
}
