import XCTest
import ImageIO
@testable import NashiCore

// "Remove all" must not publish a " - Clean" copy that still carries location.
// ExifTool is replaced (NASHI_EXIFTOOL) by a fake that reports success for every
// write. FAKE_MODE=noop writes nothing; strip replaces the file with a GPS-free
// encode of the same pixels. FAKE_CLEAN_JSON overrides the post-write read and
// FAKE_BAD_READ makes it unparseable.
final class ImageMetadataRemoveAllLocationTests: XCTestCase {
    private static let fake = #"""
    #!/usr/bin/perl
    use strict;
    if (@ARGV && $ARGV[0] eq '-config') { shift @ARGV; shift @ARGV }
    my $file=$ARGV[-1];
    sub slurp { my $p=shift; return '' unless defined $p; open my $h,'<:raw',$p or return ''; local $/; my $d=<$h>; return $d }
    my %a=map {$_=>1} @ARGV;
    if ($a{'-ImageDataHash'}) { print qq([{"SourceFile":"x","ImageDataHash":"feedface"}]); exit 0 }
    if ($a{'-b'}) { exit 0 }
    if ($a{'-j'}) {
      if (slurp($file) eq slurp($ENV{FAKE_GPS_FILE})) {
        print qq([{"SourceFile":"x","File:FileType":"JPEG","GPS:GPSLatitude":35.6595,"GPS:GPSLatitudeRef":"N","GPS:GPSLongitude":139.7005,"GPS:GPSLongitudeRef":"E","Composite:GPSPosition":"35.6595 139.7005"}]);
      } elsif ($ENV{FAKE_BAD_READ}) { print "Warning: not json" }
      else { print($ENV{FAKE_CLEAN_JSON} || qq([{"SourceFile":"x","File:FileType":"JPEG"}])) }
      exit 0
    }
    if ($a{'-overwrite_original'}) {
      if (($ENV{FAKE_MODE}||'') eq 'strip') { open my $o,'>:raw',$file or die; print $o slurp($ENV{FAKE_CLEAN_FILE}); close $o }
      print "    1 image files updated\n"; exit 0
    }
    exit 0
    """#
    private static let variables = ["NASHI_EXIFTOOL", "FAKE_GPS_FILE", "FAKE_CLEAN_FILE", "FAKE_MODE", "FAKE_CLEAN_JSON", "FAKE_BAD_READ"]
    private var saved: [String: String] = [:]
    private var folder: URL!
    private var input: URL!

    override func setUpWithError() throws {
        for name in Self.variables { if let value = ProcessInfo.processInfo.environment[name] { saved[name] = value } }
        folder = try OutputFiles.temporaryDirectory()
        let tool = folder.appendingPathComponent("exiftool")
        try Self.fake.write(to: tool, atomically: true, encoding: .utf8)
        let context = try XCTUnwrap(CGContext(data: nil, width: 16, height: 16, bitsPerComponent: 8, bytesPerRow: 64,
                                              space: CGColorSpace(name: CGColorSpace.sRGB)!,
                                              bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 0, green: 0.5, blue: 1, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 16, height: 16))
        let image = try XCTUnwrap(context.makeImage())
        input = folder.appendingPathComponent("Holiday.jpg")
        let clean = folder.appendingPathComponent("clean.jpg")
        try ImageEngine.write(image, to: input, quality: 1, metadata: [kCGImagePropertyGPSDictionary: [
            kCGImagePropertyGPSLatitude: 35.6595, kCGImagePropertyGPSLatitudeRef: "N",
            kCGImagePropertyGPSLongitude: 139.7005, kCGImagePropertyGPSLongitudeRef: "E"]] as CFDictionary)
        try ImageEngine.write(image, to: clean, quality: 1)
        XCTAssertTrue(hasGPS(input)); XCTAssertFalse(hasGPS(clean))
        setenv("NASHI_EXIFTOOL", tool.path, 1)
        setenv("FAKE_GPS_FILE", input.path, 1)
        setenv("FAKE_CLEAN_FILE", clean.path, 1)
        unsetenv("FAKE_CLEAN_JSON"); unsetenv("FAKE_BAD_READ")
    }
    override func tearDownWithError() throws {
        for name in Self.variables { if let value = saved[name] { setenv(name, value, 1) } else { unsetenv(name) } }
        try? FileManager.default.removeItem(at: folder)
    }

    private func hasGPS(_ url: URL) -> Bool {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil) else { return false }
        return (0..<CGImageSourceGetCount(source)).contains {
            (CGImageSourceCopyPropertiesAtIndex(source, $0, nil) as? [String: Any])?[kCGImagePropertyGPSDictionary as String] != nil
        }
    }
    private func removeAll(_ updates: [String: String] = [:]) throws -> URL {
        var changes = ImageMetadataOptions(); changes.removeAll = true; changes.updates = updates
        var options = OperationOptions(); options.outputDirectory = folder
        return try ImageMetadataEngine.edit(input, changes: changes, options: options, token: .init())
    }
    private func outputs() throws -> [String] {
        try FileManager.default.contentsOfDirectory(atPath: folder.path).filter { $0.hasPrefix("Holiday - ") }
    }

    func testRemoveAllRefusesToPublishWhenGPSSurvivesASuccessfulExifToolExit() throws {
        setenv("FAKE_MODE", "noop", 1)
        let original = try Data(contentsOf: input)
        for _ in 0..<2 {
            XCTAssertThrowsError(try removeAll()) { XCTAssertTrue("\($0)".contains("Location metadata could not be removed"), "\($0)") }
        }
        XCTAssertEqual(try outputs(), [])
        XCTAssertEqual(try Data(contentsOf: input), original)
    }

    func testRemoveAllRefusesWhenOnlyExifToolStillReportsAnXMPOrIPTCLocation() throws {
        setenv("FAKE_MODE", "strip", 1)
        for leftover in [#"[{"SourceFile":"x","XMP-photoshop:City":"Tokyo"}]"#,
                         #"[{"SourceFile":"x","IPTC:Sub-location":"Shibuya"}]"#,
                         #"[{"SourceFile":"x","XMP-exif:GPSLatitude":35.6595}]"#] {
            setenv("FAKE_CLEAN_JSON", leftover, 1)
            XCTAssertThrowsError(try removeAll(), leftover)
        }
        XCTAssertEqual(try outputs(), [])
    }

    func testRemoveAllFailsClosedWhenTheVerificationReadIsUnparseable() throws {
        setenv("FAKE_MODE", "strip", 1); setenv("FAKE_BAD_READ", "1", 1)
        XCTAssertThrowsError(try removeAll())
        XCTAssertEqual(try outputs(), [])
    }

    func testRemoveAllPublishesWhenLocationIsGoneOrExplicitlyReAdded() throws {
        setenv("FAKE_MODE", "strip", 1)
        let clean = try removeAll()
        XCTAssertEqual(clean.lastPathComponent, "Holiday - Clean.jpg")
        XCTAssertFalse(hasGPS(clean))
        // A user who removes everything and then adds a latitude asked for location.
        setenv("FAKE_MODE", "noop", 1)
        XCTAssertNoThrow(try removeAll(["latitude": "35.6595"]))
    }
}
