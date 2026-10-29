import XCTest
import Foundation
import PDFKit
import AppKit
@testable import NashiCore

final class PDFPreservationRegressionTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws { root = try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { if let root { try? FileManager.default.removeItem(at: root) } }

    /// Verifies that interleaving rich sources retains independent field registries and attachments, and refuses scaling.
    func testWorkspaceInterleavesRichSourcesAndPreservesIndependentFormsAndAttachments() throws {
        let first=try fixture("Workspace first",prefix:"LEFT")
        let second=try fixture("Workspace second",prefix:"RIGHT")
        var options=OperationOptions();options.outputDirectory=root
        options.pdfWorkspacePages=[.init(sourceIndex:0,pageIndex:1,rotation:90),.init(sourceIndex:1,pageIndex:0),.init(sourceIndex:0,pageIndex:0),.init(sourceIndex:1,pageIndex:1)]
        let output=try ConversionEngine.run(.init(inputs:[first,second],operation:.tool(.organizePDF),options:options))[0]
        let document=try PDFEngine.open(output),snapshot=try snapshot(output)
        XCTAssertEqual(document.pageCount,4);XCTAssertEqual(document.page(at:0)?.rotation,90)
        XCTAssertTrue(document.page(at:0)!.string!.contains("SECOND_SECRET_PAGE"))
        XCTAssertTrue(document.page(at:1)!.string!.contains("FIRST_PUBLIC_PAGE"))
        XCTAssertEqual(snapshot.fields.count,4);XCTAssertEqual(Set(snapshot.fields.map(\.name)).count,4)
        XCTAssertEqual(Set(snapshot.fields.map(\.value)),["LEFT_FIRST_VALUE","LEFT_SECOND_PRIVATE_VALUE","RIGHT_FIRST_VALUE","RIGHT_SECOND_PRIVATE_VALUE"])
        XCTAssertEqual(snapshot.attachments.count,2)
        options.pdfWidthMode = .narrowest
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[first,second],operation:.tool(.organizePDF),options:options)))
    }

    /// Refusing rich encrypted sources must roll back the entire workspace and preserve every input.
    func testWorkspaceRejectsRichEncryptedSourceWithoutPublishingOrChangingInputs()throws {
        let plain=try fixture("Workspace encrypted form",prefix:"RICH")
        let encrypted=root.appendingPathComponent("Encrypted rich workspace.pdf")
        XCTAssertTrue(try PDFEngine.open(plain).write(to:encrypted,withOptions:[.ownerPasswordOption:"synthetic-owner",.userPasswordOption:""]))
        let ordinary=try fixture("Ordinary workspace source",prefix:"PLAIN",fields:false,attachment:false)
        var options=OperationOptions()
        options.pdfWorkspacePages=[.init(sourceIndex:0,pageIndex:0),.init(sourceIndex:1,pageIndex:0)]
        try assertRefused(.organizePDF,inputs:[ordinary,encrypted],options:options,reason:"password-protected PDF contains document features")
    }

    func testOrganizePreservesRegisteredFieldsAttachmentsPageOrderAndRotations() throws {
        let input = try fixture("Reorder", prefix: "LEFT")
        let before = try Data(contentsOf: input)
        var options = OperationOptions()
        options.pdfPageOrder = [1, 0]
        options.pdfPageRotations = [0: 90, 1: 180]
        let output = try export(.organizePDF, inputs: [input], options: options)
        let document = try PDFEngine.open(output)
        XCTAssertTrue(document.page(at: 0)?.string?.contains("SECOND_SECRET_PAGE") == true)
        XCTAssertTrue(document.page(at: 1)?.string?.contains("FIRST_PUBLIC_PAGE") == true)
        XCTAssertEqual(document.page(at: 0)?.rotation, 90)
        XCTAssertEqual(document.page(at: 1)?.rotation, 180)
        let snapshot = try snapshot(output)
        XCTAssertEqual(snapshot.fields.count, 2)
        XCTAssertEqual(Set(snapshot.fields.map(\.value)), ["LEFT_FIRST_VALUE", "LEFT_SECOND_PRIVATE_VALUE"])
        XCTAssertEqual(snapshot.attachments.map(\.payload), [Data("LEFT_ATTACHMENT_PAYLOAD".utf8)])
        XCTAssertEqual(snapshot.graph.pageCount, document.pageCount)
        XCTAssertEqual(try Data(contentsOf: input), before)
    }

    func testSplitDropsDiscardedFieldAndEveryReachableRemovedPageObject() throws {
        let input = try fixture("Split", prefix: "SPLIT")
        let before = try Data(contentsOf: input)
        var options = OperationOptions()
        options.pageRanges = "1"
        let output = try export(.splitPDF, inputs: [input], options: options)
        let document = try PDFEngine.open(output)
        XCTAssertEqual(document.pageCount, 1)
        XCTAssertTrue(document.string?.contains("FIRST_PUBLIC_PAGE") == true)
        XCTAssertFalse(document.string?.contains("SECOND_SECRET_PAGE") == true)
        let snapshot = try snapshot(output)
        XCTAssertEqual(snapshot.fields.map(\.name), ["Account"])
        XCTAssertEqual(snapshot.fields.map(\.value), ["SPLIT_FIRST_VALUE"])
        XCTAssertEqual(snapshot.graph.pageCount, 1, "A removed page must not survive behind a field or annotation reference")
        XCTAssertFalse(snapshot.graph.strings.contains { $0.contains("SECOND_PRIVATE_VALUE") })
        XCTAssertFalse(snapshot.graph.streams.contains { $0.range(of: Data("SECOND_SECRET_PAGE".utf8)) != nil })
        XCTAssertEqual(snapshot.attachments.map(\.payload), [Data("SPLIT_ATTACHMENT_PAYLOAD".utf8)])
        XCTAssertEqual(try Data(contentsOf: input), before)
    }

    func testCrossPageReplyRefusesRatherThanHidingDiscardedPageContent() throws {
        let input = try fixture("Reply references discarded page", prefix: "REPLY", fields: false, attachment: false, crossPageReply: true)
        var options = OperationOptions()
        options.pageRanges = "1"
        try assertRefused(.splitPDF, inputs: [input], options: options, reason: "references to removed pages")
    }

    func testMergeSameNamedFieldsAndAttachmentsPreservesDistinctValuesAndPayloads() throws {
        let first = try fixture("First source", prefix: "LEFT")
        let second = try fixture("Second source", prefix: "RIGHT")
        let originals = try [first, second].map { try Data(contentsOf: $0) }
        let output = try export(.mergePDF, inputs: [first, second])
        let document = try PDFEngine.open(output)
        XCTAssertEqual(document.pageCount, 4)
        let snapshot = try snapshot(output)
        XCTAssertEqual(snapshot.fields.count, 4)
        XCTAssertEqual(Set(snapshot.fields.map(\.name)).count, 4, "Same-named source fields must be independently addressable")
        XCTAssertEqual(Set(snapshot.fields.map(\.value)), ["LEFT_FIRST_VALUE", "LEFT_SECOND_PRIVATE_VALUE", "RIGHT_FIRST_VALUE", "RIGHT_SECOND_PRIVATE_VALUE"])
        XCTAssertEqual(snapshot.attachments.count, 2)
        XCTAssertEqual(Set(snapshot.attachments.map(\.name)).count, 2)
        XCTAssertEqual(Set(snapshot.attachments.map(\.payload)), [Data("LEFT_ATTACHMENT_PAYLOAD".utf8), Data("RIGHT_ATTACHMENT_PAYLOAD".utf8)])
        XCTAssertEqual(snapshot.graph.pageCount, 4)
        for (index, input) in [first, second].enumerated() { XCTAssertEqual(try Data(contentsOf: input), originals[index]) }
    }

    func testDuplicatedPageFieldsAreIndependentAndAttachmentIsNotDuplicated() throws {
        let input = try fixture("Duplicate", prefix: "COPY")
        let before = try Data(contentsOf: input)
        var options = OperationOptions()
        options.pdfPageOrder = [0, 0]
        let output = try export(.organizePDF, inputs: [input], options: options)
        let snapshot = try snapshot(output)
        XCTAssertEqual(snapshot.fields.count, 2)
        XCTAssertEqual(Set(snapshot.fields.map(\.name)).count, 2)
        XCTAssertEqual(Set(snapshot.fields.map(\.address)).count, 2)
        XCTAssertTrue(snapshot.fields.allSatisfy { $0.value == "COPY_FIRST_VALUE" })
        XCTAssertEqual(snapshot.attachments.count, 1)
        XCTAssertEqual(snapshot.graph.pageCount, 2)
        XCTAssertFalse(snapshot.graph.strings.contains { $0.contains("SECOND_PRIVATE_VALUE") })

        let document = try PDFEngine.open(output)
        let first = try XCTUnwrap(document.page(at: 0)?.annotations.first { $0.type == "Widget" })
        let second = try XCTUnwrap(document.page(at: 1)?.annotations.first { $0.type == "Widget" })
        first.widgetStringValue = "CHANGED_FIRST_COPY"
        XCTAssertEqual(second.widgetStringValue, "COPY_FIRST_VALUE")
        let edited = root.appendingPathComponent("Edited independent copies.pdf")
        XCTAssertTrue(document.write(to: edited))
        let reopened = try PDFEngine.open(edited)
        XCTAssertEqual(reopened.page(at: 0)?.annotations.first { $0.type == "Widget" }?.widgetStringValue, "CHANGED_FIRST_COPY")
        XCTAssertEqual(reopened.page(at: 1)?.annotations.first { $0.type == "Widget" }?.widgetStringValue, "COPY_FIRST_VALUE")
        XCTAssertEqual(try Data(contentsOf: input), before)
    }

    func testLaterUnsupportedPDFRollsBackEarlierSuccessfulExport() throws {
        let first = try fixture("Plain first", prefix: "FIRST", fields: false, attachment: false)
        let unsupported = try fixture("Tagged second", prefix: "TAGGED", fields: false, attachment: false, tagged: true)
        var options = OperationOptions()
        options.pdfPageOrder = [1, 0]
        try assertRefused(.organizePDF, inputs: [first, unsupported], options: options, reason: "accessibility tags")
    }

    func testUniformWidthRefusesRichDataAndStillScalesPlainPages() throws {
        let rich = try fixture("Rich uniform width", prefix: "RICH")
        var options = OperationOptions()
        options.pdfUniformWidth = true
        try assertRefused(.organizePDF, inputs: [rich], options: options, reason: "Uniform page width")

        let plain = try fixture("Plain uniform width", prefix: "PLAIN", fields: false, attachment: false)
        let output = try export(.organizePDF, inputs: [plain], options: options)
        let document = try PDFEngine.open(output)
        XCTAssertEqual(document.pageCount, 2)
        let widths = (0..<document.pageCount).compactMap { document.page(at: $0)?.bounds(for: .mediaBox).width }
        XCTAssertEqual(widths.count, 2)
        XCTAssertEqual(widths[0], widths[1], accuracy: 0.01)
        XCTAssertTrue(document.string?.contains("FIRST_PUBLIC_PAGE") == true)
        XCTAssertTrue(document.string?.contains("SECOND_SECRET_PAGE") == true)
    }

    func testInvalidSelectionAndPreCancelledTransformDoNotTouchSources() throws {
        let input = try fixture("Invalid or cancelled", prefix: "UNCHANGED")
        let before = try Data(contentsOf: input)
        XCTAssertThrowsError(try PDFPreservingCodec.transform([input], selections: [[0, 99]]))
        let token = CancellationToken()
        token.cancel()
        XCTAssertThrowsError(try PDFPreservingCodec.transform([input], selections: [[0]], token: token)) { error in
            XCTAssertEqual(error.localizedDescription, NashiError.cancelled.localizedDescription)
        }
        XCTAssertEqual(try Data(contentsOf: input), before)
    }

    func testCustomPageLabelsRefuseMergeFromEitherSourceButIdentityKeepsLabels() throws {
        let plain = try fixture("Plain labels merge", prefix: "PLAIN", fields: false, attachment: false)
        let labelled = try fixture("Annex labels", prefix: "LABELLED", fields: false, attachment: false, pageLabels: true)
        for inputs in [[plain, labelled], [labelled, plain]] {
            try assertRefused(.mergePDF, inputs: inputs, options: .init(), reason: "custom page labels while merging documents")
        }
        let output = try export(.organizePDF, inputs: [labelled])
        let document = try XCTUnwrap(CGPDFDocument(output as CFURL))
        let catalog = try XCTUnwrap(document.catalog)
        var labels: CGPDFDictionaryRef?, numbers: CGPDFArrayRef?, first: CGPDFDictionaryRef?, prefix: CGPDFStringRef?
        XCTAssertTrue(CGPDFDictionaryGetDictionary(catalog, "PageLabels", &labels))
        let dictionary = try XCTUnwrap(labels)
        XCTAssertTrue(CGPDFDictionaryGetArray(dictionary, "Nums", &numbers))
        let array = try XCTUnwrap(numbers)
        XCTAssertTrue(CGPDFArrayGetDictionary(array, 1, &first))
        XCTAssertTrue(CGPDFDictionaryGetString(try XCTUnwrap(first), "P", &prefix))
        XCTAssertEqual(try XCTUnwrap(CGPDFStringCopyTextString(try XCTUnwrap(prefix))) as String, "ANNEX-")
    }

    func testOrdinaryPasswordPDFKeepsOrganizeSplitAndMergeWithSuppliedPassword() throws {
        let plain = try fixture("Ordinary encrypted source", prefix: "LOCKED", fields: false, attachment: false)
        let document = try PDFEngine.open(plain)
        let encrypted = root.appendingPathComponent("Password protected ordinary.pdf")
        XCTAssertTrue(document.write(to: encrypted, withOptions: [.ownerPasswordOption: "synthetic-owner", .userPasswordOption: "synthetic-password"]))
        XCTAssertTrue(try XCTUnwrap(PDFDocument(url: encrypted)).isLocked)
        let before = try Data(contentsOf: encrypted)
        var wrong = OperationOptions()
        wrong.password = "incorrect"
        try assertRefused(.organizePDF, inputs: [encrypted], options: wrong, reason: "password protected")

        var options = OperationOptions()
        options.password = "synthetic-password"
        options.pdfPageOrder = [1, 0]
        options.pdfPageRotations = [0: 90]
        let organized = try PDFEngine.open(export(.organizePDF, inputs: [encrypted], options: options))
        XCTAssertEqual(organized.pageCount, 2)
        XCTAssertTrue(organized.page(at: 0)?.string?.contains("SECOND_SECRET_PAGE") == true)
        XCTAssertTrue(organized.page(at: 1)?.string?.contains("FIRST_PUBLIC_PAGE") == true)
        XCTAssertEqual(organized.page(at: 0)?.rotation, 90)

        options.pageRanges = "2"
        let split = try PDFEngine.open(export(.splitPDF, inputs: [encrypted], options: options))
        XCTAssertEqual(split.pageCount, 1)
        XCTAssertTrue(split.string?.contains("SECOND_SECRET_PAGE") == true)
        let merged = try PDFEngine.open(export(.mergePDF, inputs: [encrypted, plain], options: options))
        XCTAssertEqual(merged.pageCount, 4)
        XCTAssertTrue(merged.page(at: 0)?.string?.contains("FIRST_PUBLIC_PAGE") == true)
        XCTAssertTrue(merged.page(at: 2)?.string?.contains("FIRST_PUBLIC_PAGE") == true)
        XCTAssertEqual(try Data(contentsOf: encrypted), before)
    }

    func testRichPasswordPDFRefusesPageEditingWithoutLosingFieldsOrAttachments() throws {
        let source = try fixture("Rich encrypted source", prefix: "LOCKED_RICH")
        let document = try PDFEngine.open(source)
        let encrypted = root.appendingPathComponent("Password protected rich.pdf")
        XCTAssertTrue(document.write(to: encrypted, withOptions: [.ownerPasswordOption: "synthetic-owner", .userPasswordOption: "synthetic-password"]))
        var options = OperationOptions()
        options.password = "synthetic-password"
        options.pdfPageOrder = [1, 0]
        try assertRefused(.organizePDF, inputs: [encrypted], options: options, reason: "password-protected PDF contains document features")
        options.pageRanges = "1"
        try assertRefused(.splitPDF, inputs: [encrypted], options: options, reason: "password-protected PDF contains document features")
        try assertRefused(.mergePDF, inputs: [encrypted, source], options: options, reason: "password-protected PDF contains document features")
        let reopened = try PDFEngine.open(encrypted, password: "synthetic-password")
        XCTAssertTrue(reopened.page(at: 0)?.annotations.contains { $0.type == "Widget" && $0.widgetStringValue == "LOCKED_RICH_FIRST_VALUE" } == true)
    }

    private func export(_ tool: ToolKind, inputs: [URL], options: OperationOptions = .init()) throws -> URL {
        var options = options
        options.outputDirectory = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        let outputs = try ConversionEngine.run(.init(inputs: inputs, operation: .tool(tool), options: options))
        return try XCTUnwrap(outputs.first)
    }

    private func assertRefused(_ tool: ToolKind, inputs: [URL], options: OperationOptions, reason: String) throws {
        let originals = try inputs.map { try Data(contentsOf: $0) }
        let destination = root.appendingPathComponent(UUID().uuidString, isDirectory: true)
        try FileManager.default.createDirectory(at: destination, withIntermediateDirectories: true)
        let previous = destination.appendingPathComponent("Previous.pdf")
        let previousBytes = Data("PRESERVE_USER_EXPORT".utf8)
        try previousBytes.write(to: previous)
        var options = options
        options.outputDirectory = destination
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs: inputs, operation: .tool(tool), options: options))) { error in
            XCTAssertTrue(error.localizedDescription.contains(reason), error.localizedDescription)
        }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(at: destination, includingPropertiesForKeys: nil).map(\.lastPathComponent), ["Previous.pdf"])
        XCTAssertEqual(try Data(contentsOf: previous), previousBytes)
        for (index, input) in inputs.enumerated() { XCTAssertEqual(try Data(contentsOf: input), originals[index]) }
    }

    /// A hand-written, unencrypted ordinary AcroForm fixture avoids depending on
    /// the writer under test or any build-directory / third-party test assets.
    private func fixture(_ name: String, prefix: String, fields: Bool = true, attachment: Bool = true, tagged: Bool = false, crossPageReply: Bool = false, pageLabels: Bool = false) throws -> URL {
        func stream(_ content: String, _ attributes: String = "") -> String { "<< /Length \(content.utf8.count) \(attributes) >>\nstream\n\(content)\nendstream" }
        func appearance(_ value: String) -> String { stream("BT /F1 11 Tf 4 8 Td (\(value)) Tj ET", "/Type /XObject /Subtype /Form /BBox [0 0 180 24] /Resources << /Font << /F1 5 0 R >> >>") }
        let objects = [
            "<< /Type /Catalog /Pages 2 0 R \(fields ? "/AcroForm 12 0 R" : "") \(attachment ? "/Names << /EmbeddedFiles << /Names [(Essential.txt) 13 0 R] >> >>" : "") \(tagged ? "/StructTreeRoot 17 0 R /MarkInfo << /Marked true >>" : "") \(pageLabels ? "/PageLabels << /Nums [0 << /S /r /P (ANNEX-) >>] >>" : "") >>",
            "<< /Type /Pages /Kids [3 0 R 4 0 R] /Count 2 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 360 240] /Resources << /Font << /F1 5 0 R >> >> /Contents 6 0 R /Annots [\(fields ? "8 0 R" : "") \(crossPageReply ? "15 0 R" : "")] >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 480 240] /Resources << /Font << /F1 5 0 R >> >> /Contents 7 0 R /Annots [\(fields ? "9 0 R" : "") \(crossPageReply ? "16 0 R" : "")] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            stream("BT /F1 14 Tf 20 190 Td (FIRST_PUBLIC_PAGE) Tj ET"),
            stream("BT /F1 14 Tf 20 190 Td (SECOND_SECRET_PAGE) Tj ET"),
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Account) /V (\(prefix)_FIRST_VALUE) /P 3 0 R /Rect [20 100 200 124] /DA (/F1 11 Tf 0 g) /AP << /N 10 0 R >> >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Private) /V (\(prefix)_SECOND_PRIVATE_VALUE) /P 4 0 R /Rect [20 100 200 124] /DA (/F1 11 Tf 0 g) /AP << /N 11 0 R >> >>",
            appearance(prefix + "_FIRST_VALUE"),
            appearance(prefix + "_SECOND_PRIVATE_VALUE"),
            "<< /Fields [8 0 R 9 0 R] /DR << /Font << /F1 5 0 R >> >> /DA (/F1 11 Tf 0 g) >>",
            "<< /Type /Filespec /F (Essential.txt) /UF (Essential.txt) /EF << /F 14 0 R >> >>",
            stream(prefix + "_ATTACHMENT_PAYLOAD", "/Type /EmbeddedFile"),
            "<< /Type /Annot /Subtype /Text /P 3 0 R /Rect [4 4 20 20] /Contents (PUBLIC_REPLY) /IRT 16 0 R >>",
            "<< /Type /Annot /Subtype /Text /P 4 0 R /Rect [4 4 20 20] /Contents (SECRET_REMOVED_NOTE) >>",
            "<< /Type /StructTreeRoot /K [] >>"
        ]
        var bytes = Data("%PDF-1.7\n".utf8), offsets: [Int] = []
        for (index, object) in objects.enumerated() {
            offsets.append(bytes.count)
            bytes.append(Data("\(index + 1) 0 obj\n\(object)\nendobj\n".utf8))
        }
        let xref = bytes.count
        bytes.append(Data("xref\n0 \(objects.count + 1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { bytes.append(Data(String(format: "%010d 00000 n \n", offset).utf8)) }
        bytes.append(Data("trailer\n<< /Size \(objects.count + 1) /Root 1 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let url = root.appendingPathComponent(name).appendingPathExtension("pdf")
        try bytes.write(to: url)
        return url
    }

    private struct Field { let name: String; let value: String; let address: UInt }
    private struct Attachment { let name: String; let payload: Data }
    private struct Snapshot { let fields: [Field]; let attachments: [Attachment]; let graph: Graph }

    private func snapshot(_ url: URL) throws -> Snapshot {
        let document = try XCTUnwrap(CGPDFDocument(url as CFURL))
        let catalog = try XCTUnwrap(document.catalog)
        let graph = Graph()
        graph.visit(catalog)
        var fields: [Field] = [], attachments: [Attachment] = []
        func string(_ dictionary: CGPDFDictionaryRef, _ key: String) -> String? {
            var value: CGPDFStringRef?
            guard CGPDFDictionaryGetString(dictionary, key, &value), let value, let text = CGPDFStringCopyTextString(value) else { return nil }
            return text as String
        }
        func visitFields(_ array: CGPDFArrayRef, prefix: String = "") {
            for index in 0..<CGPDFArrayGetCount(array) {
                var dictionary: CGPDFDictionaryRef?
                guard CGPDFArrayGetDictionary(array, index, &dictionary), let dictionary else { continue }
                let partial = string(dictionary, "T") ?? ""
                let name = [prefix, partial].filter { !$0.isEmpty }.joined(separator: ".")
                var kids: CGPDFArrayRef?
                if CGPDFDictionaryGetArray(dictionary, "Kids", &kids), let kids { visitFields(kids, prefix: name) }
                else if !partial.isEmpty { fields.append(Field(name: name, value: string(dictionary, "V") ?? "", address: unsafeBitCast(dictionary, to: UInt.self))) }
            }
        }
        var form: CGPDFDictionaryRef?, array: CGPDFArrayRef?
        if CGPDFDictionaryGetDictionary(catalog, "AcroForm", &form), let form, CGPDFDictionaryGetArray(form, "Fields", &array), let array { visitFields(array) }
        var names: CGPDFDictionaryRef?, embedded: CGPDFDictionaryRef?, pairs: CGPDFArrayRef?
        if CGPDFDictionaryGetDictionary(catalog, "Names", &names), let names,
           CGPDFDictionaryGetDictionary(names, "EmbeddedFiles", &embedded), let embedded,
           CGPDFDictionaryGetArray(embedded, "Names", &pairs), let pairs {
            for index in stride(from: 0, to: CGPDFArrayGetCount(pairs) - 1, by: 2) {
                var filename: CGPDFStringRef?, spec: CGPDFDictionaryRef?, ef: CGPDFDictionaryRef?, stream: CGPDFStreamRef?
                guard CGPDFArrayGetString(pairs, index, &filename), let filename,
                      let text = CGPDFStringCopyTextString(filename),
                      CGPDFArrayGetDictionary(pairs, index + 1, &spec), let spec,
                      CGPDFDictionaryGetDictionary(spec, "EF", &ef), let ef,
                      CGPDFDictionaryGetStream(ef, "F", &stream), let stream else { continue }
                var format = CGPDFDataFormat.raw
                if let data = CGPDFStreamCopyData(stream, &format) { attachments.append(Attachment(name: text as String, payload: data as Data)) }
            }
        }
        return Snapshot(fields: fields, attachments: attachments, graph: graph)
    }

    /// Traverse through CoreGraphics, independently of the preserving writer.
    /// The page tree count alone misses discarded pages retained by annotation
    /// replies, field parents, threads, or other ordinary object references.
    private final class Graph {
        var pageCount = 0
        var strings: [String] = []
        var streams: [Data] = []
        private var dictionaries = Set<UInt>()
        private var arrays = Set<UInt>()

        func visit(_ dictionary: CGPDFDictionaryRef) {
            guard dictionaries.insert(unsafeBitCast(dictionary, to: UInt.self)).inserted else { return }
            var type: UnsafePointer<CChar>?
            if CGPDFDictionaryGetName(dictionary, "Type", &type), let type, String(cString: type) == "Page" { pageCount += 1 }
            CGPDFDictionaryApplyFunction(dictionary, { _, object, pointer in
                guard let pointer else { return }
                Unmanaged<Graph>.fromOpaque(pointer).takeUnretainedValue().visitObject(object)
            }, Unmanaged.passUnretained(self).toOpaque())
        }

        private func visitObject(_ object: CGPDFObjectRef) {
            switch CGPDFObjectGetType(object) {
            case .dictionary:
                var dictionary: CGPDFDictionaryRef?
                if CGPDFObjectGetValue(object, .dictionary, &dictionary), let dictionary { visit(dictionary) }
            case .array:
                var array: CGPDFArrayRef?
                if CGPDFObjectGetValue(object, .array, &array), let array, arrays.insert(unsafeBitCast(array, to: UInt.self)).inserted {
                    for index in 0..<CGPDFArrayGetCount(array) {
                        var child: CGPDFObjectRef?
                        if CGPDFArrayGetObject(array, index, &child), let child { visitObject(child) }
                    }
                }
            case .stream:
                var stream: CGPDFStreamRef?
                if CGPDFObjectGetValue(object, .stream, &stream), let stream {
                    if let dictionary = CGPDFStreamGetDictionary(stream) { visit(dictionary) }
                    var format = CGPDFDataFormat.raw
                    if let data = CGPDFStreamCopyData(stream, &format) { streams.append(data as Data) }
                }
            case .string:
                var value: CGPDFStringRef?
                if CGPDFObjectGetValue(object, .string, &value), let value, let text = CGPDFStringCopyTextString(value) { strings.append(text as String) }
            default: break
            }
        }
    }
}
