import Foundation
import JavaScriptCore
import PDFKit

/// Object-graph page editing for ordinary AcroForms and embedded files.
/// A fresh JavaScriptCore context is used per output; no Node process or network runs.
enum PDFPreservingCodec {
    /// One selection for each source. Repeated indices create independently editable
    /// field copies. Rotations are absolute degrees indexed by the final output page.
    static func transform(_ inputs: [URL], selections: [[Int]], rotations: [Int: Int] = [:],
                          outputOrder: [Int]? = nil, crops: [Int: PDFPageBox] = [:],
                          widthMode:PDFWidthMode = .original, customWidth:Double = 595.276,
                          token: CancellationToken = .init()) throws -> Data {
        try token.check()
        guard !inputs.isEmpty, inputs.count == selections.count else {
            throw NashiError.invalidInput("Choose PDF sources and a page selection for each source.")
        }
        var sources: [String] = [], totalBytes: Int64 = 0, pageCount = 0
        for (index, input) in inputs.enumerated() {
            try token.check()
            let values = try input.resourceValues(forKeys: [.isRegularFileKey, .fileSizeKey])
            guard values.isRegularFile == true, let size = values.fileSize, size > 0, size <= 100_000_000 else {
                throw NashiError.invalidInput("This PDF is empty, is not a regular file, or exceeds the 100 MB page-editing limit.")
            }
            totalBytes += Int64(size)
            guard totalBytes <= 200_000_000 else {
                throw NashiError.invalidInput("These PDFs exceed the 200 MB combined page-editing limit.")
            }
            guard !selections[index].isEmpty, selections[index].count <= 10_000 else {
                throw NashiError.invalidInput("Choose between 1 and 10,000 output pages.")
            }
            pageCount += selections[index].count
            guard pageCount <= 10_000 else { throw NashiError.invalidInput("Choose no more than 10,000 output pages at once.") }
            sources.append(try Data(contentsOf: input, options: [.mappedIfSafe]).base64EncodedString())
        }
        var request: [String: Any] = [
            "sources": sources, "selections": selections,
            "rotations": Dictionary(uniqueKeysWithValues: rotations.map { (String($0.key), $0.value) })
        ]
        if let outputOrder { request["outputOrder"]=outputOrder }
        request["widthMode"]=widthMode.rawValue;request["customWidth"]=customWidth
        request["crops"]=Dictionary(uniqueKeysWithValues:crops.map{ key,box in (String(key),["x":box.x,"y":box.y,"width":box.width,"height":box.height]) })
        let json = try JSONSerialization.data(withJSONObject: request)
        let engine = try Runtime(token: token)
        let encoded = try engine.transform(String(decoding: json, as: UTF8.self))
        try token.check()
        guard encoded.count <= 700_000_000, let bytes = Data(base64Encoded: encoded),
              bytes.starts(with: Data("%PDF-".utf8)), let document = PDFDocument(data: bytes),
              document.pageCount == pageCount else {
            throw NashiError.failed("The PDF page writer returned an invalid document. No copy was exported.")
        }
        return bytes
    }

    private final class Runtime {
        private let context: JSContext
        private let token: CancellationToken
        private var result: String?
        private var diagnostic: String?

        init(token: CancellationToken) throws {
            self.token = token
            guard let context = JSContext() else { throw NashiError.failed("The native PDF runtime could not start.") }
            self.context = context
            context.exceptionHandler = { [weak self] _, exception in self?.diagnostic = exception?.toString() ?? "Unknown PDF runtime error." }
            let cancelled: @convention(block) () -> Bool = { token.isCancelled }
            let complete: @convention(block) (String) -> Void = { [weak self] in self?.result = $0 }
            let failed: @convention(block) (String) -> Void = { [weak self] in self?.diagnostic = $0 }
            context.setObject(cancelled, forKeyedSubscript: "_nashiPDFCancelled" as NSString)
            context.setObject(complete, forKeyedSubscript: "_nashiPDFComplete" as NSString)
            context.setObject(failed, forKeyedSubscript: "_nashiPDFFailed" as NSString)
            var roots: [URL] = [Bundle.main.resourceURL].compactMap{$0}
            #if DEBUG
            let source = URL(fileURLWithPath: #filePath).deletingLastPathComponent().deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("Resources")
            roots.append(source)
            #endif
            guard let root = roots.first(where: { root in
                ["PDFLib.js", "PDFInterop.js"].allSatisfy { FileManager.default.fileExists(atPath: root.appendingPathComponent($0).path) }
            }) else { throw NashiError.missingDependency("the bundled preserving PDF library") }
            for filename in ["PDFLib.js", "PDFInterop.js"] {
                try token.check()
                let script = root.appendingPathComponent(filename)
                context.evaluateScript(try String(contentsOf: script, encoding: .utf8), withSourceURL: script)
                if let diagnostic { throw NashiError.failed("The native PDF library could not load: \(diagnostic)") }
            }
        }

        /// Invokes the bundled object-graph writer and checks cancellation, runtime diagnostics and completion deadlines.
        func transform(_ json: String) throws -> String {
            try token.check()
            guard let run = context.objectForKeyedSubscript("NashiPDF")?.objectForKeyedSubscript("run"), !run.isUndefined else {
                throw NashiError.failed("The native PDF writer is unavailable.")
            }
            _ = run.call(withArguments: [json])
            // Promise continuations normally finish at the JS call boundary. This
            // bounded loop also permits a delayed microtask checkpoint. It is not
            // a hard watchdog around a synchronous JavaScript parser invocation.
            let deadline = Date().addingTimeInterval(120)
            while result == nil, diagnostic == nil, Date() < deadline {
                try token.check()
                _ = RunLoop.current.run(mode: .default, before: Date().addingTimeInterval(0.01))
            }
            try token.check()
            if let diagnostic {
                if diagnostic.hasPrefix("This PDF contains ") { throw NashiError.unsupported(diagnostic) }
                throw NashiError.invalidInput("The PDF could not be safely edited: \(diagnostic)")
            }
            guard let result else { throw NashiError.failed("The native PDF writer did not finish. No copy was exported.") }
            return result
        }
    }
}

