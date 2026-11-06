import XCTest
import ImageIO
@testable import NashiCore

// The metadata editor lists ImageIO names next to ExifTool names, for example
// {Exif}.CameraOwnerName next to ExifTool's {Exif}.OwnerName. ExifTool is replaced
// (NASHI_EXIFTOOL) by a fake that knows only ExifTool's tag names: a known removal
// swaps in an ImageIO-encoded variant without that tag, an unknown one only warns,
// and the run fails only when nothing known was requested, as ExifTool does.
final class ImageMetadataImageIONameRemovalTests: XCTestCase {
    private static let fake = #"""
    #!/usr/bin/perl
    use strict;
    if (@ARGV && $ARGV[0] eq '-config') { shift @ARGV; shift @ARGV }
    my $dir=$ENV{FAKE_DIR};
    my %value=('IFD0:Artist'=>'Holiday Author','ExifIFD:OwnerName'=>'Ayse Yilmaz','ExifIFD:SerialNumber'=>'SN-12345');
    my $file=$ARGV[-1];
    sub tags { my $f=shift; if (open my $h,'<',"$dir/state/".unpack('H*',$f)) { local $/; my $d=<$h>; return grep {length} split /\n/,$d } return sort keys %value }
    my %a=map {$_=>1} @ARGV;
    if ($a{'-ImageDataHash'}) { print qq([{"SourceFile":"x","ImageDataHash":"feedface"}]); exit 0 }
    if ($a{'-b'}) { exit 0 }
    if ($a{'-j'}) { print '[{"SourceFile":"x"'.join('',map {qq(,"$_":"$value{$_}")} tags($file)).'}]'; exit 0 }
    if ($a{'-overwrite_original'}) {
      my @t=tags($file); my $known=0;
      for my $arg (@ARGV) { next unless $arg=~/^-(.+)=$/; my $tag=$1; if (exists $value{$tag}) { @t=grep {$_ ne $tag} @t; $known++ } else { print STDERR "Warning: Tag '$tag' is not defined\n" } }
      unless ($known) { print STDERR "Nothing to do.\n"; exit 1 }
      mkdir "$dir/state"; open my $s,'>',"$dir/state/".unpack('H*',$file) or die; print $s join("\n",@t); close $s;
      open my $in,'<:raw',"$dir/variant_".join('+',map {(split /:/)[1]} sort @t).".jpg" or die; local $/; my $d=<$in>; close $in;
      open my $o,'>:raw',$file or die; print $o $d; close $o;
      print "    1 image files updated\n"; exit 0
    }
    exit 0
    """#
    private static let variables = ["NASHI_EXIFTOOL", "FAKE_DIR"]
    private var saved: [String: String] = [:]
    private var folder: URL!

    override func setUpWithError() throws {
        for name in Self.variables { if let value = ProcessInfo.processInfo.environment[name] { saved[name] = value } }
        folder = try OutputFiles.temporaryDirectory()
        let tool = folder.appendingPathComponent("exiftool")
        try Data(Self.fake.utf8).write(to: tool)
        setenv("NASHI_EXIFTOOL", tool.path, 1)
        setenv("FAKE_DIR", folder.path, 1)
        try jpeg(folder.appendingPathComponent("variant_.jpg"), artist: false, owner: false)
        try jpeg(folder.appendingPathComponent("variant_OwnerName+SerialNumber.jpg"), artist: false, owner: true)
    }

    override func tearDownWithError() throws {
        for name in Self.variables { if let value = saved[name] { setenv(name, value, 1) } else { unsetenv(name) } }
        try? FileManager.default.removeItem(at: folder)
    }

    private func jpeg(_ url: URL, artist: Bool, owner: Bool) throws {
        let context = try XCTUnwrap(CGContext(data: nil, width: 24, height: 16, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 0.2, green: 0.5, blue: 0.7, alpha: 1))
        context.fill(CGRect(x: 0, y: 0, width: 24, height: 16))
        var properties: [CFString: Any] = [:]
        if artist { properties[kCGImagePropertyTIFFDictionary] = [kCGImagePropertyTIFFArtist: "Holiday Author"] }
        if owner { properties[kCGImagePropertyExifDictionary] = [kCGImagePropertyExifCameraOwnerName: "Ayse Yilmaz", kCGImagePropertyExifBodySerialNumber: "SN-12345"] }
        let destination = try XCTUnwrap(CGImageDestinationCreateWithURL(url as CFURL, "public.jpeg" as CFString, 1, nil))
        CGImageDestinationAddImage(destination, try XCTUnwrap(context.makeImage()), properties as CFDictionary)
        XCTAssertTrue(CGImageDestinationFinalize(destination))
    }

    private func exif(_ url: URL) throws -> [String: Any] {
        let source = try XCTUnwrap(CGImageSourceCreateWithURL(url as CFURL, nil))
        return ((CGImageSourceCopyPropertiesAtIndex(source, 0, nil) as? [String: Any])?[kCGImagePropertyExifDictionary as String] as? [String: Any]) ?? [:]
    }

    func testRemovingImageIONamedOwnerAndSerialIsVerifiedBeforeSaving() throws {
        let input = folder.appendingPathComponent("Holiday.jpg")
        try jpeg(input, artist: true, owner: true)
        let original = try Data(contentsOf: input)
        let output = folder.appendingPathComponent("Output")
        try FileManager.default.createDirectory(at: output, withIntermediateDirectories: true)
        var options = OperationOptions(); options.outputDirectory = output
        let listed = try ImageEngine.readMetadata(input)
        XCTAssertEqual(listed["{Exif}.CameraOwnerName"], "Ayse Yilmaz")
        XCTAssertEqual(listed["{Exif}.OwnerName"], "Ayse Yilmaz")

        // Removing only the ImageIO-named rows must not save a copy that still has them.
        var imageIONames = ImageMetadataOptions()
        imageIONames.removals = ["{TIFF}.Artist", "{Exif}.CameraOwnerName", "{Exif}.BodySerialNumber"]
        XCTAssertThrowsError(try ImageEngine.editMetadata(input, changes: imageIONames, options: options)) { error in
            XCTAssertTrue("\(error)".contains("could not be removed"), "\(error)")
        }
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: output.path), [])
        XCTAssertEqual(try Data(contentsOf: input), original)

        // Removing both names of each field writes a copy without them.
        var bothNames = imageIONames
        bothNames.removals += ["{Exif}.OwnerName", "{Exif}.SerialNumber"]
        let cleaned = try ImageEngine.editMetadata(input, changes: bothNames, options: options)
        XCTAssertNil(try exif(cleaned)[kCGImagePropertyExifCameraOwnerName as String])
        XCTAssertNil(try exif(cleaned)[kCGImagePropertyExifBodySerialNumber as String])
        try FileManager.default.removeItem(at: cleaned)

        // Fields that were not requested may stay.
        var artistOnly = ImageMetadataOptions(); artistOnly.removals = ["{TIFF}.Artist"]
        let edited = try ImageEngine.editMetadata(input, changes: artistOnly, options: options)
        XCTAssertEqual(try exif(edited)[kCGImagePropertyExifCameraOwnerName as String] as? String, "Ayse Yilmaz")
    }
}
