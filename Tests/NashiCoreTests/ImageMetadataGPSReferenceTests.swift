import XCTest
import ImageIO
@testable import NashiCore

// The metadata editor lists GPS coordinates as ImageIO and ExifTool store them: an
// unsigned {GPS}.Latitude next to a separate {GPS}.LatitudeRef row. ExifTool is
// replaced (NASHI_EXIFTOOL) by a fake that keeps tag values in a state file, reports
// them for -j reads and applies "-TAG=VALUE" writes.
final class ImageMetadataGPSReferenceTests: XCTestCase {
    private static let fake = #"""
    #!/usr/bin/perl
    use strict;
    use JSON::PP;
    if (@ARGV && $ARGV[0] eq '-config') { shift @ARGV; shift @ARGV }
    my $state="$ENV{FAKE_DIR}/state.json";
    open my $h,'<',$state or die; local $/; my %tags=%{decode_json(<$h>)}; close $h;
    my %a=map {$_=>1} @ARGV;
    if ($a{'-ImageDataHash'}) { print qq([{"SourceFile":"x","ImageDataHash":"feedface"}]); exit 0 }
    if ($a{'-b'}) { exit 0 }
    if ($a{'-j'}) { print encode_json([{SourceFile=>'x',%tags}]); exit 0 }
    if ($a{'-overwrite_original'}) {
      for my $arg (@ARGV) { next unless $arg=~/^-([A-Za-z0-9_]+:[A-Za-z0-9_]+)=(.*)$/s; if (length $2) { $tags{$1}=$2 } else { delete $tags{$1} } }
      open my $o,'>',$state or die; print $o encode_json(\%tags); close $o;
      print "    1 image files updated\n"; exit 0
    }
    exit 0
    """#
    private static let variables = ["NASHI_EXIFTOOL", "FAKE_DIR"]
    private static let southWest = ["GPS:GPSLatitude": "33.865", "GPS:GPSLatitudeRef": "S", "GPS:GPSLongitude": "70.65", "GPS:GPSLongitudeRef": "W"]
    private var saved: [String: String] = [:]
    private var folder: URL!
    private var input: URL!

    override func setUpWithError() throws {
        for name in Self.variables { if let value = ProcessInfo.processInfo.environment[name] { saved[name] = value } }
        folder = try OutputFiles.temporaryDirectory()
        let tool = folder.appendingPathComponent("exiftool")
        try Data(Self.fake.utf8).write(to: tool)
        setenv("NASHI_EXIFTOOL", tool.path, 1)
        setenv("FAKE_DIR", folder.path, 1)
        input = folder.appendingPathComponent("Photo.jpg")
        let context = try XCTUnwrap(CGContext(data: nil, width: 24, height: 16, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 0.2, green: 0.5, blue: 0.7, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 24, height: 16))
        let destination = try XCTUnwrap(CGImageDestinationCreateWithURL(input as CFURL, "public.jpeg" as CFString, 1, nil))
        CGImageDestinationAddImage(destination, try XCTUnwrap(context.makeImage()), nil)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
    }

    override func tearDownWithError() throws {
        for name in Self.variables { if let value = saved[name] { setenv(name, value, 1) } else { unsetenv(name) } }
        try? FileManager.default.removeItem(at: folder)
    }

    private func edit(_ tags: [String: String], _ updates: [String: String]) throws -> [String: String] {
        let state = folder.appendingPathComponent("state.json")
        try JSONSerialization.data(withJSONObject: tags).write(to: state)
        let output = folder.appendingPathComponent(UUID().uuidString)
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        var options = OperationOptions(); options.outputDirectory = output
        var changes = ImageMetadataOptions(); changes.updates = updates
        _ = try ImageEngine.editMetadata(input, changes: changes, options: options)
        return try XCTUnwrap(JSONSerialization.jsonObject(with: Data(contentsOf: state)) as? [String: String])
    }

    func testEditingAListedCoordinateKeepsItsHemisphere() throws {
        let latitude = try edit(Self.southWest, ["{GPS}.Latitude": "33.87"])
        XCTAssertEqual(latitude["GPS:GPSLatitude"], "33.87")
        XCTAssertEqual(latitude["GPS:GPSLatitudeRef"], "S")
        XCTAssertEqual(try edit(Self.southWest, ["{GPS}.Longitude": "70.7"])["GPS:GPSLongitudeRef"], "W")
        XCTAssertEqual(try edit(Self.southWest, ["{GPS}.Latitude": "33.87", "{GPS}.LatitudeRef": "N"])["GPS:GPSLatitudeRef"], "N")
        XCTAssertEqual(try edit(["GPS:GPSLatitude": "10", "GPS:GPSLatitudeRef": "N"], ["{GPS}.Latitude": "-10.5"])["GPS:GPSLatitudeRef"], "S")
    }

    func testSignedAliasesAndNewCoordinatesStillWriteAReference() throws {
        let alias = try edit(Self.southWest, ["latitude": "33.87", "longitude": "70.7"])
        XCTAssertEqual(alias["GPS:GPSLatitudeRef"], "N")
        XCTAssertEqual(alias["GPS:GPSLongitudeRef"], "E")
        let added = try edit([:], ["{GPS}.Latitude": "12.5", "{GPS}.Longitude": "8.25"])
        XCTAssertEqual(added["GPS:GPSLatitudeRef"], "N")
        XCTAssertEqual(added["GPS:GPSLongitudeRef"], "E")
    }
}
