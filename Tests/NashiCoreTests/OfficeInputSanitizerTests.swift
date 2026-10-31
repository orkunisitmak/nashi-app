import XCTest
@testable import NashiCore

final class OfficeInputSanitizerTests: XCTestCase {
    private let secret = "nashi-secret"

    private func package(_ files: [String: String], named name: String = "input") throws -> (input: URL, base: URL) {
        let base = try OutputFiles.temporaryDirectory()
        let root = base.appendingPathComponent("Package", isDirectory: true)
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
        for (path, body) in files {
            let item = root.appendingPathComponent(path)
            try FileManager.default.createDirectory(at: item.deletingLastPathComponent(), withIntermediateDirectories: true)
            try body.write(to: item, atomically: true, encoding: .utf8)
        }
        let input = base.appendingPathComponent(name + ".docx")
        try ArchiveCodec.create(from: root, at: input, format: "zip", token: .init())
        return (input, base)
    }

    private func sanitizedParts(_ input: URL, extensions: [String], base: URL) throws -> [String: String] {
        let output = base.appendingPathComponent(UUID().uuidString + ".docx")
        defer { try? FileManager.default.removeItem(at: output) }
        try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
        let unpacked = base.appendingPathComponent(UUID().uuidString, isDirectory: true)
        defer { try? FileManager.default.removeItem(at: unpacked) }
        try ArchiveCodec.extract(output, into: unpacked, token: .init())
        var parts: [String: String] = [:]
        for ext in extensions {
            // enumerator(at:) hands back resolved paths ("/private/var/..." for a "/var/..."
            // temporary directory), so the keys come from the relative enumerator instead.
            guard let walk = FileManager.default.enumerator(atPath: unpacked.path) else { continue }
            for case let relative as String in walk where (relative as NSString).pathExtension.lowercased() == ext {
                parts[relative] = try DocumentText.read(unpacked.appendingPathComponent(relative))
            }
        }
        return parts
    }

    func testHostPathRelationshipsAreRemovedForEveryAcceptedSpelling() throws {
        let rels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rIdDouble" Type="image" Target="file:///tmp/\(secret).png" TargetMode="External"/>
        <Relationship Id="rIdSingle" Type="image" Target='file:///tmp/\(secret).png' TargetMode='External'/>
        <Relationship Id="rIdEntity" Type="image" Target="&#102;ile:///tmp/\(secret).png" TargetMode="External"/>
        <Relationship Id="rIdSpacing" Type="image" Target = "file:///tmp/\(secret).png" TargetMode = "External"/>
        <Relationship Id="rIdDotDot" Type="image" Target="../../tmp/\(secret).png" TargetMode="External"/>
        <Relationship Id="rIdDrive" Type="image" Target="c:\\tmp\\\(secret).png" TargetMode="External"/>
        <Relationship Id="rIdUnc" Type="image" Target="\\\\server\\share\\\(secret).png" TargetMode="External"/>
        <Relationship Id="rIdMedia" Type="image" Target="media/picture.png"/>
        <Relationship Id="rIdLink" Type="hyperlink" Target="https://example.com/" TargetMode="External"/>
        </Relationships>
        """
        let (input, base) = try package(["_rels/document.xml.rels": rels, "word/document.xml": "<a/>"])
        defer { try? FileManager.default.removeItem(at: base) }
        let parts = try sanitizedParts(input, extensions: ["rels"], base: base)
        let text = try XCTUnwrap(parts["_rels/document.xml.rels"], "The relationship part never reached the staged package")
        for removed in ["rIdDouble", "rIdSingle", "rIdEntity", "rIdSpacing", "rIdDotDot", "rIdDrive", "rIdUnc"] {
            XCTAssertFalse(text.contains(removed), "\(removed) still points at a host path")
        }
        XCTAssertTrue(text.contains("rIdMedia"))
        XCTAssertTrue(text.contains("rIdLink"), "An inert external hyperlink must survive conversion")
    }

    func testHostPathAttributesAreNeutralisedWithoutRewritingOtherParts() throws {
        let document = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <w:document xmlns:xlink="http://www.w3.org/1999/xlink">
        <w:body><img xlink:href="file:///tmp/\(secret).png"/><img xl:href='file:///tmp/\(secret).png'/>        <img x:href="&#47;etc/passwd"/><img xlink:href="c:\\tmp\\\(secret).png"/><span data-src="file:///tmp/\(secret)-kept.png"/></w:body></w:document>
        """
        let vector = "<svg xmlns=\"http://www.w3.org/2000/svg\" xmlns:xlink=\"http://www.w3.org/1999/xlink\"><image xlink:href=\"file:///tmp/\(secret).png\"/></svg>"
        let (input, base) = try package(["word/document.xml": document, "word/media/image1.svg": vector])
        defer { try? FileManager.default.removeItem(at: base) }
        let parts = try sanitizedParts(input, extensions: ["xml", "svg"], base: base)
        let text = try XCTUnwrap(parts["word/document.xml"], "The document part never reached the staged package")
        XCTAssertEqual(text.components(separatedBy: "about:blank").count - 1, 4)
        XCTAssertTrue(text.contains(#"xlink:href="about:blank""#))
        XCTAssertTrue(text.contains("xl:href='about:blank'"), "The original quote style has to be restored")
        XCTAssertTrue(text.contains(#"data-src="file:///tmp/\#(secret)-kept.png""#), "data-src is not a resource reference")
        XCTAssertFalse(text.contains(secret + ".png"))
        let embedded = try XCTUnwrap(parts["word/media/image1.svg"], "The SVG part never reached the staged package")
        XCTAssertTrue(embedded.contains("about:blank"), "An SVG part resolves its own xlink:href")
        XCTAssertFalse(embedded.contains(secret))
    }

    func testHostPathXMLBaseIsNeutralisedAndRelativeBasesSurvive() throws {
        let document = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink" xml:base="file:///tmp/\(secret)/">
        <draw:image xlink:href="picture.png"/>
        <draw:frame xml:base='&#102;ile:///tmp/\(secret)/'><draw:image xlink:href="other.png"/></draw:frame>
        <draw:frame xml:base="images/"><draw:image xlink:href="inside.png"/></draw:frame>
        </office:document-content>
        """
        let (input, base) = try package(["content.xml": document], named: "anchored")
        defer { try? FileManager.default.removeItem(at: base) }
        let parts = try sanitizedParts(input, extensions: ["xml"], base: base)
        let text = try XCTUnwrap(parts["content.xml"], "The content part never reached the staged package")
        XCTAssertTrue(text.contains("xml:base=\"about:blank\""), "A host-path base keeps anchoring every target below it")
        XCTAssertTrue(text.contains("xml:base='about:blank'"), "The original quote style has to be restored")
        XCTAssertTrue(text.contains("xml:base=\"images/\""), "A document-internal base is not a host path")
        XCTAssertEqual(text.components(separatedBy: "about:blank").count - 1, 2)
        for kept in ["picture.png", "other.png", "inside.png"] {
            XCTAssertTrue(text.contains(kept), kept + " is document-internal and must survive")
        }
        XCTAssertFalse(text.contains(secret), "The base value must not survive in the staged part")
    }

    /// Rewriting text instead of an attribute deletes the markup around it: an
    /// unquoted value match runs to the next quote in the file, and that quote can
    /// belong to a later tag, so the replacement swallows the closing tag and the
    /// element behind it. The prose below is the shape a reviewer reported, and a
    /// document-internal reference follows it, so the text has to survive verbatim
    /// while the real attribute in the same part is still stripped.
    func testAttributeLikeTextSurvivesAndRealAttributesAreStillStripped() throws {
        let base = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: base) }
        let flat = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><office:body><office:text><text:p>xml:base="file:///tmp</text:p><draw:image xlink:href="Pictures/kept.png"/><text:p>the src="file:///tmp/deep.png" in this sentence is prose</text:p><draw:image xlink:href="file:///tmp/\(secret).png"/></office:text></office:body></office:document-content>
        """
        let input = base.appendingPathComponent("prose.odt")
        try flat.write(to: input, atomically: true, encoding: .utf8)
        let output = base.appendingPathComponent(UUID().uuidString + ".odt")
        try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
        let text = try DocumentText.read(output)
        XCTAssertTrue(text.contains(#"xml:base="file:///tmp</text:p>"#), "The paragraph lost its closing tag")
        XCTAssertTrue(text.contains(#"<draw:image xlink:href="Pictures/kept.png"/>"#), "The element behind the prose was swallowed")
        XCTAssertTrue(text.contains(#"src="file:///tmp/deep.png""#), "Prose is document text, not a reference")
        XCTAssertEqual(text.components(separatedBy: "</text:p>").count - 1, 2, "A closing tag disappeared")
        XCTAssertEqual(text.components(separatedBy: "<draw:image").count - 1, 2, "An element disappeared")
        XCTAssertEqual(text.components(separatedBy: "about:blank").count - 1, 1, "Only the real attribute may be rewritten")
        XCTAssertTrue(text.contains(#"xlink:href="about:blank""#), String(text.prefix(200)))
        XCTAssertFalse(text.contains(secret))
    }

    /// A quote that sits in prose must not desynchronise the scan: if it opened a
    /// value that ran across the next tag, the host path inside that tag would stay
    /// untouched and still be fetched by the converter. A `>` inside a quoted value
    /// is the mirror case - it may not end the tag either.
    func testAQuoteInProseCannotHideAHostPathReference() throws {
        let base = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: base) }
        let flat = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><text:p>an inch is written " and an apostrophe's too</text:p><draw:image xlink:href="file:///tmp/\(secret)-hidden.png"/><draw:image xlink:href="file:///tmp/edge>\(secret)-quoted.png"/></office:document-content>
        """
        let input = base.appendingPathComponent("quotes.odt")
        try flat.write(to: input, atomically: true, encoding: .utf8)
        let output = base.appendingPathComponent(UUID().uuidString + ".odt")
        try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
        let text = try DocumentText.read(output)
        XCTAssertEqual(text.components(separatedBy: "<draw:image").count - 1, 2, "An element disappeared")
        XCTAssertEqual(text.components(separatedBy: "about:blank").count - 1, 2, "Both host-path references must be neutralised")
        XCTAssertFalse(text.contains(secret), "A host path survived behind a prose quote")
    }

    /// A `<!DOCTYPE` is the one spelling the sanitizer cannot see through: an
    /// internal DTD subset can bind `&host;` to a host path, and
    /// `decodeCharacterReferences` leaves that name literal, so `isHostPath` finds
    /// nothing while the converter's parser expands the entity and loads the file.
    /// Real OOXML/ODF parts never carry a DOCTYPE, so the part is refused whole -
    /// and only a real declaration is refused, not the same text inside a comment.
    func testPackagePartDeclaringADocumentTypeIsRefused() throws {
        let declaration = #"<!DOCTYPE office:document-content [<!ENTITY host "file:///tmp/#" + secret + #".png">]>"#
        let declared = """
        <?xml version="1.0" encoding="UTF-8"?>
        \(declaration)
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><draw:image xlink:href="&host;"/></office:document-content>
        """
        let fixture = try package(["word/document.xml": declared, "word/_rels/document.xml.rels": "<Relationships/>"])
        defer { try? FileManager.default.removeItem(at: fixture.base) }
        let output = fixture.base.appendingPathComponent(UUID().uuidString + ".docx")
        XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(fixture.input, output: output, token: .init())) { error in
            guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
            XCTAssertTrue(message.contains("DOCTYPE"), message)
        }

        // The identical text inside a comment is not a declaration: the part has to
        // convert, the comment has to come back untouched, and the real host
        // attribute next to it still has to be rewritten.
        let commented = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!-- \(declaration) -->
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><draw:image xlink:href="file:///tmp/\(secret).png"/></office:document-content>
        """
        let control = try package(["word/document.xml": commented, "word/_rels/document.xml.rels": "<Relationships/>"])
        defer { try? FileManager.default.removeItem(at: control.base) }
        let parts = try sanitizedParts(control.input, extensions: ["xml"], base: control.base)
        let staged = try XCTUnwrap(parts["word/document.xml"])
        XCTAssertTrue(staged.contains("<!-- \(declaration) -->"), String(staged.prefix(200)))
        // The preserved comment quotes the host path itself, so the secret name cannot
        // be the check here. What has to be gone is a reference the parser would use.
        XCTAssertTrue(staged.contains(#"xlink:href="about:blank""#), "the real attribute next to the comment must be neutralised")
        XCTAssertFalse(staged.contains(#"href="file:"#), "a live host target survived")
    }

    /// The flat path takes the same verdict, and a CDATA section is the mirror case
    /// of the comment above: markup-looking bytes that no parser will read as a
    /// declaration must not refuse the document, and must survive verbatim.
    func testFlatInputDeclaringADocumentTypeIsRefused() throws {
        let base = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: base) }
        let declaration = #"<!DOCTYPE office:document-content [<!ENTITY host "file:///tmp/#" + secret + #".png">]>"#
        let declared = """
        <?xml version="1.0" encoding="UTF-8"?>
        \(declaration)
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><draw:image xlink:href="&host;"/></office:document-content>
        """
        let input = base.appendingPathComponent("declared.odt")
        try declared.write(to: input, atomically: true, encoding: .utf8)
        let output = base.appendingPathComponent(UUID().uuidString + ".odt")
        XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(input, output: output, token: .init())) { error in
            guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
            XCTAssertTrue(message.contains("DOCTYPE"), message)
        }

        let quoted = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><text:p><![CDATA[\(declaration)]]></text:p><draw:image xlink:href="file:///tmp/\(secret).png"/></office:document-content>
        """
        let cdataInput = base.appendingPathComponent("quoted.odt")
        try quoted.write(to: cdataInput, atomically: true, encoding: .utf8)
        let cdataOutput = base.appendingPathComponent(UUID().uuidString + ".odt")
        try OfficeInputSanitizer.sanitize(cdataInput, output: cdataOutput, token: .init())
        let staged = try DocumentText.read(cdataOutput)
        XCTAssertTrue(staged.contains("<![CDATA[\(declaration)]]>"), String(staged.prefix(200)))
        // Same reason as in the comment case: the CDATA section is kept verbatim and it
        // quotes the host path, so only a reference a parser would resolve has to go.
        XCTAssertTrue(staged.contains(#"xlink:href="about:blank""#), "the real attribute next to the CDATA section must be neutralised")
        XCTAssertFalse(staged.contains(#"href="file:"#), "a live host target survived")
    }

    /// A drawn SVG part is the exception: the converter refuses to load one whose standard `PUBLIC`
    /// declaration is gone, so a declaration that defines nothing survives sanitising here while the drawing's
    /// own host reference is still rewritten. Refusing these files whole was the reported bug.
    func testSvgPartKeepingAPublicDocumentTypeIsSanitisedNotRefused() throws {
        let drawing = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" width="3cm" height="3cm"><image xlink:href="file:///tmp/\(secret).png" width="3cm" height="3cm"/><text x="0" y="2">A&nbsp;B</text></svg>
        """
        let fixture = try package(["word/document.xml": "<w:document/>", "word/media/diagram.svg": drawing])
        defer { try? FileManager.default.removeItem(at: fixture.base) }
        let parts = try sanitizedParts(fixture.input, extensions: ["svg"], base: fixture.base)
        let staged = try XCTUnwrap(parts["word/media/diagram.svg"])
        XCTAssertTrue(staged.contains("<!DOCTYPE svg PUBLIC"), "the declaration the converter needs was dropped")
        XCTAssertTrue(staged.contains(#"xlink:href="about:blank""#), "the drawing's host reference must still be neutralised")
    }

    /// The tolerance stops where a declaration could define something. An internal subset and a `SYSTEM`
    /// literal both leave the converter an entity to expand - the file a `SYSTEM` literal names can travel
    /// inside the same package - so an SVG part carrying either is refused exactly like every other markup part.
    func testSvgPartDeclaringAnExpandableDocumentTypeIsRefused() throws {
        let subset = #"<!DOCTYPE svg [<!ENTITY host "file:///tmp/"# + secret + #".png">]>"#
        let system = #"<!DOCTYPE svg SYSTEM "evil.dtd">"#
        for declaration in [subset, system] {
            let drawing = """
            <?xml version="1.0" encoding="UTF-8"?>
            \(declaration)
            <svg xmlns="http://www.w3.org/1999/xlink" xmlns="http://www.w3.org/2000/svg"><image xlink:href="&host;"/></svg>
            """
            let fixture = try package(["word/document.xml": "<w:document/>", "word/media/diagram.svg": drawing])
            defer { try? FileManager.default.removeItem(at: fixture.base) }
            let output = fixture.base.appendingPathComponent(UUID().uuidString + ".docx")
            XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(fixture.input, output: output, token: .init()), declaration) { error in
                guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
                XCTAssertTrue(message.contains("DOCTYPE"), message)
            }
        }
    }

    /// A `PUBLIC` declaration carries its location in a plain quoted string, so a `file:///` path sitting in
    /// that position never meets the `SYSTEM` keyword test, and `stripReferences` does not look inside a
    /// declaration at all. The absolute spelling, the package-relative one - with the named DTD actually
    /// shipped beside the drawing - and an empty location are all refused: what is pinned is where a DTD may
    /// come from, not which keyword introduced it.
    func testSvgPartDeclaringADocumentTypeWithANonStandardLocationIsRefused() throws {
        let fileLocation = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "file:///etc/x.dtd">"#
        let relativeLocation = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "local.dtd">"#
        let emptyLocation = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "">"#
        for declaration in [fileLocation, relativeLocation, emptyLocation] {
            let drawing = """
            <?xml version="1.0" encoding="UTF-8"?>
            \(declaration)
            <svg xmlns="http://www.w3.org/2000/svg"><text x="0" y="2">A&nbsp;B</text></svg>
            """
            let fixture = try package([
                "word/document.xml": "<w:document/>",
                "word/media/diagram.svg": drawing,
                "word/media/local.dtd": "<!ENTITY host SYSTEM \"file:///etc/hosts\">",
            ])
            defer { try? FileManager.default.removeItem(at: fixture.base) }
            let output = fixture.base.appendingPathComponent(UUID().uuidString + ".docx")
            XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(fixture.input, output: output, token: .init()), declaration) { error in
                guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
                XCTAssertTrue(message.contains("DOCTYPE"), message)
            }
        }
    }

    /// The two spellings that name a location without quoting one, and the one that carries no location
    /// literal at all. Measured, the converter reads no declaration in these forms: the drawing is dropped from
    /// the page and nothing is fetched, so they are refused rather than exported as a conversion with a hole.
    func testSvgPartDeclaringALocationThatLooksLikeAPublicIdentifierIsRefused() throws {
        // The location is checked by position, so a literal that opens like an identifier is still a place when it
        // sits in the location slot, and a package can ship the file it names. A third literal is refused too,
        // because a PUBLIC declaration has nothing else to quote.
        let dashed = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "-//x.dtd">"#
        let plus = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "+//x.dtd">"#
        let third = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd" "local.dtd">"#
        for declaration in [dashed, plus, third] {
            let drawing = """
            <?xml version="1.0" encoding="UTF-8"?>
            \(declaration)
            <svg xmlns="http://www.w3.org/2000/svg"><text x="0" y="2">A&nbsp;B</text></svg>
            """
            let fixture = try package([
                "word/document.xml": "<w:document/>",
                "word/media/diagram.svg": drawing,
                "word/media/local.dtd": "<!ENTITY host SYSTEM \"file:///etc/hosts\">",
            ])
            defer { try? FileManager.default.removeItem(at: fixture.base) }
            let output = fixture.base.appendingPathComponent(UUID().uuidString + ".docx")
            XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(fixture.input, output: output, token: .init()), declaration) { error in
                guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
                XCTAssertTrue(message.contains("DOCTYPE"), message)
            }
        }
    }

    func testSvgPartDeclaringADocumentTypeWithStrayLiteralsIsRefused() throws {
        // the standard DTD, so any quoted string inside it is a location that cannot be checked.
        // A declaration that carries neither the PUBLIC keyword nor an empty literal list has no spelling that names
        let stray = #"<!DOCTYPE svg "local.dtd">"#
        let quotedName = #"<!DOCTYPE "svg">"#
        for declaration in [stray, quotedName] {
            let drawing = """
            <?xml version="1.0" encoding="UTF-8"?>
            \(declaration)
            <svg xmlns="http://www.w3.org/2000/svg"><text x="0" y="2">A&nbsp;B</text></svg>
            """
            let fixture = try package([
                "word/document.xml": "<w:document/>",
                "word/media/diagram.svg": drawing,
                "word/media/local.dtd": "<!ENTITY host SYSTEM \"file:///etc/hosts\">",
            ])
            defer { try? FileManager.default.removeItem(at: fixture.base) }
            let output = fixture.base.appendingPathComponent(UUID().uuidString + ".docx")
            XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(fixture.input, output: output, token: .init()), declaration) { error in
                guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
                XCTAssertTrue(message.contains("DOCTYPE"), message)
            }
        }
    }

    func testSvgPartDeclaringAMalformedDocumentTypeIsRefused() throws {
        let unquotedLocation = #"<!DOCTYPE svg PUBLIC -//W3C//DTD SVG 1.1//EN http://127.0.0.1:8099/x.dtd>"#
        let identifierOnly = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN">"#
        let unterminatedLocation = #"<!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "file:///etc/x.dtd>"#
        for declaration in [unquotedLocation, identifierOnly, unterminatedLocation] {
            let drawing = """
            <?xml version="1.0" encoding="UTF-8"?>
            \(declaration)
            <svg xmlns="http://www.w3.org/2000/svg"><text x="0" y="2">A&nbsp;B</text></svg>
            """
            let fixture = try package(["word/document.xml": "<w:document/>", "word/media/diagram.svg": drawing])
            defer { try? FileManager.default.removeItem(at: fixture.base) }
            let output = fixture.base.appendingPathComponent(UUID().uuidString + ".docx")
            XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(fixture.input, output: output, token: .init()), declaration) { error in
                guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
                XCTAssertTrue(message.contains("DOCTYPE"), message)
            }
        }
    }

    /// The location is pinned, the identifier is not: an export that names a different public identifier but
    /// points at the standard DTD URL has no host path to reach, so it converts like the standard declaration.
    func testSvgPartDeclaringAnotherPublicIdentifierAtTheStandardLocationIsSanitised() throws {
        let drawing = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.0//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
        <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink"><image xlink:href="file:///tmp/\(secret).png"/><text x="0" y="2">A&nbsp;B</text></svg>
        """
        let fixture = try package(["word/document.xml": "<w:document/>", "word/media/diagram.svg": drawing])
        defer { try? FileManager.default.removeItem(at: fixture.base) }
        let parts = try sanitizedParts(fixture.input, extensions: ["svg"], base: fixture.base)
        let staged = try XCTUnwrap(parts["word/media/diagram.svg"])
        XCTAssertTrue(staged.contains("<!DOCTYPE svg PUBLIC"), "a declaration pointing at the standard DTD was dropped")
        XCTAssertTrue(staged.contains(#"xlink:href="about:blank""#), "the drawing's host reference must still be neutralised")
    }

    /// A declaration with no location defines nothing and fetches nothing, so the empty form stays too.
    func testSvgPartDeclaringADocumentTypeWithoutALocationIsSanitised() throws {
        let drawing = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE svg>
        <svg xmlns="http://www.w3.org/2000/svg"><text x="0" y="2">A&nbsp;B</text></svg>
        """
        let fixture = try package(["word/document.xml": "<w:document/>", "word/media/diagram.svg": drawing])
        defer { try? FileManager.default.removeItem(at: fixture.base) }
        let parts = try sanitizedParts(fixture.input, extensions: ["svg"], base: fixture.base)
        XCTAssertTrue(try XCTUnwrap(parts["word/media/diagram.svg"]).contains("<!DOCTYPE svg>"), "the location-free declaration was dropped")
    }

    func testDashPrefixedEntryNamesAreStoredInsteadOfReachingZipOptions() throws {
        let (input, base) = try package([
            "mimetype": "application/vnd.oasis.opendocument.text",
            "-xword/document.xml": "ignored",
            "word/-TTdocument.xml": "kept",
            "content.xml": "<office/>"
        ], named: "dashed")
        defer { try? FileManager.default.removeItem(at: base) }
        let output = base.appendingPathComponent(UUID().uuidString + ".docx")
        defer { try? FileManager.default.removeItem(at: output) }
        try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
        let unpacked = base.appendingPathComponent(UUID().uuidString, isDirectory: true)
        defer { try? FileManager.default.removeItem(at: unpacked) }
        try ArchiveCodec.extract(output, into: unpacked, token: .init())
        for name in ["mimetype", "content.xml", "-xword/document.xml", "word/-TTdocument.xml"] {
            XCTAssertTrue(FileManager.default.fileExists(atPath: unpacked.appendingPathComponent(name).path), name + " was dropped from the repacked package")
        }
    }

    func testFlatMarkupSavedUnderAPackageExtensionIsStripped() throws {
        let base = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: base) }
        let flat = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink">        <draw:image xlink:href="file:///tmp/\(secret).png"/><draw:image xlink:href='file:///tmp/\(secret).png'/>        </office:document-content>
        """
        let input = base.appendingPathComponent("flat.odt")
        try flat.write(to: input, atomically: true, encoding: .utf8)
        let output = base.appendingPathComponent(UUID().uuidString + ".odt")
        try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
        let text = try DocumentText.read(output)
        XCTAssertTrue(text.hasPrefix("<?xml"))
        XCTAssertEqual(text.components(separatedBy: "about:blank").count - 1, 2)
        XCTAssertFalse(text.contains(secret))
    }

    func testRewrittenDeclarationStillDescribesTheStagedBytes() throws {
        let base = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: base) }
        let flat = """
        <?xml version="1.0" encoding='UTF-16' standalone='yes'?>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><draw:image xlink:href="file:///tmp/\(secret).png"/></office:document-content>
        """
        let input = base.appendingPathComponent("declared.odt")
        let payload = try XCTUnwrap(flat.data(using: .utf16LittleEndian))
        try (Data([0xff, 0xfe]) + payload).write(to: input)
        let output = base.appendingPathComponent(UUID().uuidString + ".odt")
        try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
        // The input really is UTF-16, so the staged file must be re-declared UTF-8 bytes.
        let staged = try Data(contentsOf: output)
        XCTAssertFalse(staged.starts(with: [0xff, 0xfe]))
        XCTAssertNotNil(String(data: staged, encoding: .utf8))
        let text = try DocumentText.read(output)
        // The staged bytes are UTF-8, and only the encoding name may change.
        XCTAssertTrue(text.hasPrefix("<?xml version=\"1.0\" encoding='UTF-8' standalone='yes'?>"), String(text.prefix(120)))
        XCTAssertEqual(text.components(separatedBy: "about:blank").count - 1, 1)
        XCTAssertFalse(text.contains(secret))
    }

    /// The CI job runs swift test with an explicit --filter list of test names, so
    /// a new class never executes unless it is added there. This asserts the list
    /// stayed in sync instead of letting the suite rot silently.
    func testContinuousIntegrationFilterRunsThisClass() throws {
        let path = URL(fileURLWithPath: #file)
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .deletingLastPathComponent()
            .appendingPathComponent(".github/workflows/source.yml")
        let text = try DocumentText.read(path)
        let filters = text.split(separator: "\n").filter { $0.contains("swift test --filter") }
        XCTAssertFalse(filters.isEmpty)
        // Each job filters its own slice of the suite, so only the archive/core
        // job has to name this class.
        XCTAssertTrue(filters.contains { $0.contains("OfficeInputSanitizerTests") },
                      "no swift test --filter line in source.yml runs OfficeInputSanitizerTests")
    }

    /// The converter decodes character references in every attribute value, not
    /// only in the target, so `Ext&#101;rnal` opens a relationship with a host
    /// path just as the spelled mode does. Decimal, hexadecimal, surrounding
    /// spacing, a single-quoted value and a capitalised remainder are all the
    /// same mode to the converter, so all of them have to be removed here.
    func testCharacterReferenceEncodedTargetModeIsRemovedLikeTheSpelledMode() throws {
        let rels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
        <Relationship Id="rIdDecimal" Type="image" Target="file:///tmp/\(secret).png" TargetMode="Ext&#101;rnal"/>
        <Relationship Id="rIdHex" Type="image" Target="file:///tmp/\(secret).png" TargetMode="&#x45;xternal"/>
        <Relationship Id="rIdSpacing" Type="image" Target="file:///tmp/\(secret).png" TargetMode = " Ext&#101;RNAL "/>
        <Relationship Id="rIdSingle" Type="image" Target='file:///tmp/\(secret).png' TargetMode='&#69;XTERNAL'/>
        <Relationship Id="rIdLiteral" Type="image" Target="file:///tmp/\(secret).png" TargetMode="External"/>
        <Relationship Id="rIdMedia" Type="image" Target="media/picture.png"/>
        <Relationship Id="rIdInternal" Type="image" Target="media/other.png" TargetMode="Internal"/>
        <Relationship Id="rIdLink" Type="hyperlink" Target="https://example.com/keep-me" TargetMode="External"/>
        </Relationships>
        """
        let (input, base) = try package(["_rels/document.xml.rels": rels, "word/document.xml": "<a/>"])
        defer { try? FileManager.default.removeItem(at: base) }
        let parts = try sanitizedParts(input, extensions: ["rels"], base: base)
        let text = try XCTUnwrap(parts["_rels/document.xml.rels"], "The relationship part never reached the staged package")
        for removed in ["rIdDecimal", "rIdHex", "rIdSpacing", "rIdSingle", "rIdLiteral"] {
            XCTAssertFalse(text.contains(removed), "\(removed) keeps a host path behind an encoded mode")
        }
        XCTAssertFalse(text.contains("file:///"), "a live host target survived: \(text)")
        XCTAssertTrue(text.contains("rIdMedia"))
        XCTAssertTrue(text.contains("rIdInternal"), "An internal target must survive whatever mode it names")
        XCTAssertTrue(text.contains("keep-me"), "An inert external hyperlink must survive conversion")
    }

    /// The converter chooses its parser from the content of the file, so the
    /// extension an input carries cannot decide whether it is inspected: the same
    /// flat markup saved as `.doc` would have reached the parser untouched.
    func testFlatMarkupSavedUnderAnotherNameIsStrippedAndBinaryKept() throws {
        let base = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: base) }
        let flat = """
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><draw:image xlink:href="file:///tmp/\(secret).png"/></office:document-content>
        """
        for name in ["legacy", "flat.rtf", "sheet", "deck", "document"] {
            let input = base.appendingPathComponent(name).appendingPathExtension(name.hasPrefix("flat.") ? "rtf" : "doc")
            try flat.write(to: input, atomically: true, encoding: .utf8)
            let output = base.appendingPathComponent(UUID().uuidString).appendingPathExtension(input.pathExtension)
            try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
            let staged = try DocumentText.read(output)
            XCTAssertEqual(staged.components(separatedBy: "about:blank").count - 1, 1, "flat markup named \(input.lastPathComponent) was not neutralised")
            XCTAssertFalse(staged.contains(secret), "flat markup named \(input.lastPathComponent) kept its host path")
        }

        // Refusal follows the same rule as the known flat shapes.
        let declared = """
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE office:document-content [<!ENTITY host "file:///tmp/\(secret).png">]>
        <office:document-content xmlns:xlink="http://www.w3.org/1999/xlink"><draw:image xlink:href="&host;"/></office:document-content>
        """
        let declaredInput = base.appendingPathComponent("declared").appendingPathExtension("doc")
        try declared.write(to: declaredInput, atomically: true, encoding: .utf8)
        XCTAssertThrowsError(try OfficeInputSanitizer.sanitize(declaredInput, output: base.appendingPathComponent(UUID().uuidString + ".doc"), token: .init())) { error in
            guard case let NashiError.invalidInput(message) = error else { return XCTFail("expected invalidInput, got \(error)") }
            XCTAssertTrue(message.contains("DOCTYPE"), message)
        }

        // A legacy binary document keeps its own bytes: the converter has to be
        // able to run its own detection on the format it is asked to open.
        let legacy = Data([0xD0, 0xCF, 0x11, 0xE0] + Array("Target=\"file:///tmp/\(secret).png\"".utf8))
        for name in ["compound", "word", "calc"] {
            let input = base.appendingPathComponent(name).appendingPathExtension("doc")
            try legacy.write(to: input)
            let output = base.appendingPathComponent(UUID().uuidString).appendingPathExtension("doc")
            try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
            XCTAssertEqual(try Data(contentsOf: output), legacy, "a binary document named \(input.lastPathComponent) was rewritten")
        }

        // The text formats reach the converter through an explicit text import, so
        // no parser is chosen from their content and their bytes have to survive.
        for name in ["notes.txt", "readme.md", "table.csv", "table.tsv"] {
            let input = base.appendingPathComponent(name)
            try flat.write(to: input, atomically: true, encoding: .utf8)
            let output = base.appendingPathComponent(UUID().uuidString).appendingPathExtension(input.pathExtension)
            try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
            XCTAssertEqual(try Data(contentsOf: output), Data(flat.utf8), "\(name) is imported as text and must not be rewritten")
        }
    }

    func testNonMarkupInputIsCopiedByteForByte() throws {
        let base = try OutputFiles.temporaryDirectory()
        defer { try? FileManager.default.removeItem(at: base) }
        let legacy = Data([0xD0, 0xCF, 0x11, 0xE0] + Array("Legacy compound document mislabelled as a package".utf8))
        let input = base.appendingPathComponent("legacy.odt")
        try legacy.write(to: input)
        let output = base.appendingPathComponent(UUID().uuidString + ".odt")
        try OfficeInputSanitizer.sanitize(input, output: output, token: .init())
        XCTAssertEqual(try Data(contentsOf: output), legacy, "A mislabelled legacy file must keep the converter's own detection")
    }
}
