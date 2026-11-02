import XCTest
@testable import NashiCore

final class WebAddressPolicyTests:XCTestCase {
    private func url(_ value:String)->URL {
        // Test fixtures are literal by design; a parse failure must fail loudly.
        URL(string:value)!
    }

    func testPrivateAndReservedIPv4LiteralsAreRejected() {
        for host in ["0.0.0.0","10.0.0.1","10.255.255.254","127.0.0.1","127.9.9.9","169.254.169.254","172.16.0.1",
                     "172.31.255.255","192.168.0.1","192.0.0.1","192.0.2.5","198.18.0.1","198.51.100.7","203.0.113.9",
                     "100.64.0.1","100.127.255.254","224.0.0.1","239.255.255.250","240.0.0.1","255.255.255.255"] {
            XCTAssertFalse(WebAddressPolicy.isPublicAddress(host),host)
            XCTAssertThrowsError(try WebAddressPolicy.validate(url("http://\(host)/media.mp4")),host)
        }
    }

    func testPublicIPv4LiteralsAreAccepted() throws {
        for host in ["1.1.1.1","8.8.8.8","93.184.216.34","172.15.0.1","172.32.0.1","192.169.0.1"] {
            XCTAssertTrue(WebAddressPolicy.isPublicAddress(host),host)
            XCTAssertNoThrow(try WebAddressPolicy.syntax(url("https://\(host)/media.mp4")),host)
        }
    }

    func testPrivateAndSpecialIPv6LiteralsAreRejected() {
        for host in ["::","::1","fc00::1","fd12:3456:789a::1","fe80::1","fec0::1","ff02::1","::ffff:127.0.0.1",
                     "::ffff:169.254.169.254","::127.0.0.1","64:ff9b::10.0.0.1","2001:db8::1","2002:7f00:1::1",
                     "2001:0:4136:e378:8000:63bf:3fff:fdd2","100::1"] {
            XCTAssertFalse(WebAddressPolicy.isPublicAddress(host),host)
            XCTAssertThrowsError(try WebAddressPolicy.validate(url("http://[\(host)]/media.mp4")),host)
        }
    }

    func testPublicIPv6LiteralsAreAccepted() throws {
        for host in ["2606:4700:4700::1111","2001:4860:4860::8888","64:ff9b::808:808","::ffff:8.8.8.8"] {
            XCTAssertTrue(WebAddressPolicy.isPublicAddress(host),host)
            XCTAssertNoThrow(try WebAddressPolicy.validate(url("https://[\(host)]/media.mp4")),host)
        }
    }

    func testLinkRejectsLocalNamesAndMetadataEndpoints() {
        for value in ["http://127.0.0.1/a.mp4","http://localhost:8080/a.mp4","http://printer.local/a.mp4",
                      "http://box.internal/a.mp4","http://nas/a.mp4","http://169.254.169.254/latest/meta-data/",
                      "http://[::1]/a.mp4","http://10.0.0.1/a.mp4","https://192.168.1.10:5000/a.mp4"] {
            XCTAssertThrowsError(try WebDownloadEngine.link(value),value)
        }
    }

    func testLinkStillAcceptsPublicAddresses() throws {
        XCTAssertEqual(try WebDownloadEngine.link("https://example.org/watch?v=1").host,"example.org")
        XCTAssertEqual(try WebDownloadEngine.link(" https://93.184.216.34/a.mp4 ").host,"93.184.216.34")
    }

    func testResolvedPrivateAddressIsRejected() {
        let address=url("https://media.example.org/clip.mp4")
        XCTAssertThrowsError(try WebAddressPolicy.validate(address,resolve:{_ in ["10.1.2.3"]}))
        XCTAssertThrowsError(try WebAddressPolicy.validate(address,resolve:{_ in ["93.184.216.34","127.0.0.1"]}))
        XCTAssertNoThrow(try WebAddressPolicy.validate(address,resolve:{_ in ["93.184.216.34"]}))
    }

    func testHostThatDoesNotResolveIsRejected() {
        XCTAssertThrowsError(try WebAddressPolicy.validate(url("https://media.example.org/clip.mp4"),resolve:{_ in []}))
    }

    func testMalformedAddressesCountAsPrivate() {
        for host in ["","not-an-address","999.1.1.1","1.1.1","fe80::1%en0"] {
            XCTAssertFalse(WebAddressPolicy.isPublicAddress(host),host)
        }
    }
}
