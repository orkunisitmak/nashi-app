import XCTest
import AppKit
import PDFKit
@testable import NashiCore

final class PDFSafetyAuditTests:XCTestCase {
    private var root:URL!
    override func setUpWithError() throws { root=try OutputFiles.temporaryDirectory() }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at:root) }

    func testRemoveAllDropsCatalogPageAndAnnotationIdentityButKeepsTextAndNavigation() throws {
        let input=try fixture(rotation:90),before=try Data(contentsOf:input)
        var options=OperationOptions();options.metadataRemoveAll=true
        let output=try XCTUnwrap(ConversionEngine.run(.init(inputs:[input],operation:.tool(.removeMetadata),options:options)).first)
        let pdf=try PDFEngine.open(output),page=try XCTUnwrap(pdf.page(at:0))
        XCTAssertTrue(pdf.string?.contains("Public report") == true)
        XCTAssertEqual(page.rotation,90)
        let attributes=String(describing:pdf.documentAttributes)
        XCTAssertFalse(attributes.contains("SECRET_INFO_AUTHOR"))
        let serialized=try Data(contentsOf:output)
        for secret in ["SECRET_INFO_AUTHOR","SECRET_XMP_AUTHOR","SECRET_PAGE_AUTHOR","SECRET_NOTE_AUTHOR"] {
            XCTAssertNil(serialized.range(of:Data(secret.utf8)),secret)
        }
        let cg=try XCTUnwrap(CGPDFDocument(output as CFURL))
        var stream:CGPDFStreamRef?
        XCTAssertFalse(CGPDFDictionaryGetStream(try XCTUnwrap(cg.catalog),"Metadata",&stream))
        XCTAssertFalse(CGPDFDictionaryGetStream(try XCTUnwrap(cg.page(at:1)?.dictionary),"Metadata",&stream))
        let note=try XCTUnwrap(page.annotations.first{$0.type=="Text"})
        XCTAssertNil(note.userName);XCTAssertNil(note.modificationDate)
        XCTAssertEqual(note.contents,"Public note")
        XCTAssertEqual((page.annotations.compactMap(\.action).first{$0 is PDFActionURL} as? PDFActionURL)?.url?.absoluteString,"https://example.com/public")
        let internalLink=try XCTUnwrap(page.annotations.compactMap(\.action).first{$0 is PDFActionGoTo} as? PDFActionGoTo)
        XCTAssertTrue(internalLink.destination.page===page)
        let bookmark=try XCTUnwrap(pdf.outlineRoot?.child(at:0))
        XCTAssertEqual(bookmark.label,"Public section");XCTAssertTrue(bookmark.destination?.page===page)
        XCTAssertEqual(try Data(contentsOf:input),before)
    }

    func testEditableFormMetadataRemovalRefusesWithoutPublishingOrChangingFields() throws {
        let input=try fixture(rotation:0,includeForm:true),before=try Data(contentsOf:input)
        let source=try PDFEngine.open(input)
        XCTAssertEqual(source.page(at:0)?.annotations.first{$0.type=="Widget"}?.fieldName,"Public field")
        var form:CGPDFDictionaryRef?
        XCTAssertTrue(CGPDFDictionaryGetDictionary(try XCTUnwrap(source.documentRef?.catalog),"AcroForm",&form))
        var options=OperationOptions();options.metadataRemoveAll=true
        let folder=root.appendingPathComponent("Chosen exports",isDirectory:true);options.outputDirectory=folder
        try FileManager.default.createDirectory(at:folder,withIntermediateDirectories:true)
        let previous=folder.appendingPathComponent("Previous.pdf"),bytes=Data("previous export".utf8)
        try bytes.write(to:previous)
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[input],operation:.tool(.removeMetadata),options:options))) { error in
            XCTAssertTrue(error.localizedDescription.contains("form fields"))
        }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(at:folder,includingPropertiesForKeys:nil).map(\.lastPathComponent),["Previous.pdf"])
        XCTAssertEqual(try Data(contentsOf:previous),bytes)
        XCTAssertEqual(try Data(contentsOf:input),before)
        XCTAssertEqual(try PDFEngine.open(input).page(at:0)?.annotations.first{$0.type=="Widget"}?.widgetStringValue,"Visible value")
    }

    func testRichCatalogCleanupRefusesWithoutLosingAttachmentsLayersOrAccessibility() throws {
        for (feature,reason) in [("attachments","embedded files"),("associatedFiles","embedded files"),("layers","optional content layers"),("tags","accessibility tags")] {
            let input=try fixture(rotation:0,catalogFeature:feature),before=try Data(contentsOf:input)
            XCTAssertTrue(try PDFEngine.open(input).string?.contains("Public report") == true)
            var options=OperationOptions();options.metadataRemoveAll=true
            let folder=root.appendingPathComponent(feature,isDirectory:true);options.outputDirectory=folder
            XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[input],operation:.tool(.removeMetadata),options:options))) { error in
                XCTAssertTrue(error.localizedDescription.contains(reason))
            }
            // The refused export also removes the output folder it created.
            XCTAssertFalse(FileManager.default.fileExists(atPath:folder.path))
            XCTAssertEqual(try Data(contentsOf:input),before)
        }
    }

    func testRasterExportUsesDisplayedRotationAndFillsTheWholePage() throws {
        for rotation in [0,90,270] {
            let input=try fixture(rotation:rotation)
            var options=OperationOptions();options.dpi=72
            let output=try XCTUnwrap(PDFEngine.render(input,to:"png",options:options,token:.init()).first)
            let image=try ImageEngine.load(output),bitmap=NSBitmapImageRep(cgImage:image)
            XCTAssertEqual(image.width,rotation==0 ? 240:120)
            XCTAssertEqual(image.height,rotation==0 ? 120:240)
            let points:[(Int,Int)]=rotation==0 ? [(60,60),(180,60)]:[(60,60),(60,180)]
            let colors=try points.map{try XCTUnwrap(bitmap.colorAt(x:$0.0,y:$0.1)?.usingColorSpace(.deviceRGB))}
            XCTAssertTrue(colors.contains{$0.redComponent>0.9 && $0.blueComponent<0.1},"Missing red half at rotation \(rotation)")
            XCTAssertTrue(colors.contains{$0.blueComponent>0.9 && $0.redComponent<0.1},"Missing blue half at rotation \(rotation)")
        }
    }

    func testLargeSearchablePDFCompressionKeepsTextAndNavigationAndRejectsDestructiveTarget() throws {
        let input=try fixture(rotation:0,padding:20_000),before=try Data(contentsOf:input)
        let folder=root.appendingPathComponent("Compressed",isDirectory:true)
        var options=OperationOptions();options.outputDirectory=folder
        let output=try XCTUnwrap(ConversionEngine.run(.init(inputs:[input],operation:.tool(.compress),options:options)).first)
        let pdf=try PDFEngine.open(output)
        XCTAssertTrue(pdf.string?.contains("Public report") == true)
        XCTAssertEqual(pdf.outlineRoot?.child(at:0)?.label,"Public section")
        XCTAssertEqual(pdf.page(at:0)?.annotations.count,3)
        XCTAssertLessThanOrEqual(try Data(contentsOf:output).count,before.count)
        let previous=try FileManager.default.contentsOfDirectory(at:folder,includingPropertiesForKeys:nil).map(\.lastPathComponent)
        options.targetBytes=64
        XCTAssertThrowsError(try ConversionEngine.run(.init(inputs:[input],operation:.tool(.compress),options:options))) { error in
            XCTAssertTrue(error.localizedDescription.contains("selectable text"))
        }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(at:folder,includingPropertiesForKeys:nil).map(\.lastPathComponent),previous)
        XCTAssertEqual(try Data(contentsOf:input),before)
    }

    /// A hand-written PDF exercises the catalog and page XMP independently of
    /// PDFKit's writer, which does not expose those packets for fixture creation.
    private func fixture(rotation:Int,padding:Int=0,includeForm:Bool=false,catalogFeature:String="") throws -> URL {
        func stream(_ body:String,_ attributes:String="")->String { "<< /Length \(body.utf8.count) \(attributes) >>\nstream\n\(body)\nendstream" }
        func xmp(_ name:String)->String { "<?xpacket begin=''?><x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description xmlns:dc='http://purl.org/dc/elements/1.1/'><dc:creator><rdf:Seq><rdf:li>\(name)</rdf:li></rdf:Seq></dc:creator></rdf:Description></rdf:RDF></x:xmpmeta><?xpacket end='w'?>" }
        let content="1 0 0 rg 0 0 120 120 re f 0 0 1 rg 120 0 120 120 re f\n0 0 0 rg BT /F1 12 Tf 12 16 Td (Public report) Tj ET\n"+String(repeating:"0 0 m 1 1 l S\n",count:padding)
        let extraCatalog:[String:String]=[
            "attachments":"/Names << /EmbeddedFiles << /Names [(Important.txt) 16 0 R] >> >>",
            "associatedFiles":"/AF [16 0 R]",
            "layers":"/OCProperties << /OCGs [16 0 R] /D << /OFF [16 0 R] >> >>",
            "tags":"/StructTreeRoot 16 0 R /MarkInfo << /Marked true >> /Lang (en-US)"
        ]
        var objects=[
            "<< /Type /Catalog /Pages 2 0 R /Metadata 5 0 R /Outlines 6 0 R \(includeForm ? "/AcroForm 15 0 R":"") \(extraCatalog[catalogFeature] ?? "") >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 240 120] /Rotate \(rotation) /Resources << /Font << /F1 4 0 R >> >> /Contents 7 0 R /Metadata 9 0 R /Annots [10 0 R 11 0 R 12 0 R \(includeForm ? "13 0 R":"")] >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            stream(xmp("SECRET_XMP_AUTHOR"),"/Type /Metadata /Subtype /XML"),
            "<< /Type /Outlines /First 8 0 R /Last 8 0 R /Count 1 >>",
            stream(content),
            "<< /Title (Public section) /Parent 6 0 R /Dest [3 0 R /XYZ 12 80 1] >>",
            stream(xmp("SECRET_PAGE_AUTHOR"),"/Type /Metadata /Subtype /XML"),
            "<< /Type /Annot /Subtype /Text /Rect [4 4 20 20] /P 3 0 R /Contents (Public note) /T (SECRET_NOTE_AUTHOR) /M (D:20000101000000Z) /CreationDate (D:20000101000000Z) >>",
            "<< /Type /Annot /Subtype /Link /Rect [20 30 60 40] /P 3 0 R /A << /S /URI /URI (https://example.com/public) >> >>",
            "<< /Type /Annot /Subtype /Link /Rect [70 30 110 40] /P 3 0 R /A << /S /GoTo /D [3 0 R /XYZ 12 80 1] >> >>",
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (Public field) /V (Visible value) /Rect [20 80 100 100] /P 3 0 R /DA (/Helv 10 Tf 0 g) >>",
            "<< /Title (Public title) /Author (SECRET_INFO_AUTHOR) >>",
            "<< /Fields [13 0 R] /DR << /Font << /Helv 4 0 R >> >> /DA (/Helv 10 Tf 0 g) >>"
        ]
        switch catalogFeature {
        case "attachments","associatedFiles":objects += ["<< /Type /Filespec /F (Important.txt) /EF << /F 17 0 R >> >>",stream("Important attachment text","/Type /EmbeddedFile")]
        case "layers":objects += ["<< /Type /OCG /Name (Private layer) >>"]
        case "tags":objects += ["<< /Type /StructTreeRoot /ParentTree 17 0 R /K [] >>","<< /Nums [] >>"]
        default:break
        }
        var data=Data("%PDF-1.7\n".utf8),offsets=[Int]()
        for (index,object) in objects.enumerated() { offsets.append(data.count);data.append(Data("\(index+1) 0 obj\n\(object)\nendobj\n".utf8)) }
        let xref=data.count
        data.append(Data("xref\n0 \(objects.count+1)\n0000000000 65535 f \n".utf8))
        for offset in offsets { data.append(Data(String(format:"%010d 00000 n \n",offset).utf8)) }
        data.append(Data("trailer\n<< /Size \(objects.count+1) /Root 1 0 R /Info 14 0 R >>\nstartxref\n\(xref)\n%%EOF\n".utf8))
        let url=root.appendingPathComponent("Fixture-\(rotation)-\(padding)-\(catalogFeature).pdf")
        try data.write(to:url);return url
    }
}
