import XCTest
import Foundation
@testable import NashiUpdates

final class GitHubUpdateTransportTests: XCTestCase {
    private let fakeToken = "github_pat_TEST_ONLY_NOT_A_CREDENTIAL"
    private let feed = Data("\u{FEFF}<?xml version=\"1.0\"?>\r\n<rss><channel/></rss>\r\n<!-- edSignature=\"fixture\" length=\"54\" -->\n".utf8)

    override func tearDown() {
        GitHubFixtureProtocol.install(nil)
        super.tearDown()
    }

    private func client(_ handler: @escaping (URLRequest) throws -> GitHubFixtureProtocol.Reply) throws -> GitHubUpdateClient {
        GitHubFixtureProtocol.install(handler)
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [GitHubFixtureProtocol.self]
        return GitHubUpdateClient(repository: try GitHubRepository("Example/Nashi"), token: fakeToken, configuration: configuration)
    }
    private func json(_ object: Any) throws -> Data { try JSONSerialization.data(withJSONObject: object, options: [.sortedKeys]) }
    private func asset(_ size: Int, name: String = "Nashi Test.zip") -> [String: Any] {
        ["id": 42, "name": name, "size": size, "state": "uploaded"]
    }
    private func session() -> URLSession {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.timeoutIntervalForRequest = 5
        configuration.timeoutIntervalForResource = 10
        return URLSession(configuration: configuration)
    }

    func testRebrandFetchesCurrentRepositoryWithoutContactingTheOldName() async throws {
        var paths:[String]=[]
        GitHubFixtureProtocol.install { request in
            let path=request.url!.path;paths.append(path)
            XCTAssertEqual(request.value(forHTTPHeaderField:"Authorization"),"Bearer "+self.fakeToken)
            XCTAssertTrue(path.hasPrefix("/repos/orkunisitmak/nashi/"))
            if path.hasSuffix("/releases/latest") { return .init(body:try self.json(["draft":false,"prerelease":false,"assets":[self.asset(self.feed.count,name:"appcast.xml")]])) }
            return .init(body:self.feed)
        }
        let configuration=URLSessionConfiguration.ephemeral;configuration.protocolClasses=[GitHubFixtureProtocol.self]
        let (client,data)=try await UpdateBrandMigration.latestFeed(repository:UpdateBrandMigration.current,token:fakeToken,configuration:configuration)
        XCTAssertEqual(client.repository,UpdateBrandMigration.current);XCTAssertEqual(data,feed);XCTAssertEqual(paths.count,2)
    }
    func testRebrandUsesTheExactPreviousRepositoryOnlyBeforeRename() async throws {
        var paths:[String]=[]
        GitHubFixtureProtocol.install { request in
            let path=request.url!.path;paths.append(path)
            XCTAssertEqual(request.url?.host,"api.github.com")
            XCTAssertEqual(request.value(forHTTPHeaderField:"Authorization"),"Bearer "+self.fakeToken)
            if path.hasPrefix("/repos/orkunisitmak/nashi/") { return .init(status:404,body:Data()) }
            XCTAssertTrue(path.hasPrefix("/repos/orkunisitmak/tangerine/"))
            if path.hasSuffix("/releases/latest") { return .init(body:try self.json(["draft":false,"prerelease":false,"assets":[self.asset(self.feed.count,name:"appcast.xml")]])) }
            return .init(body:self.feed)
        }
        let configuration=URLSessionConfiguration.ephemeral;configuration.protocolClasses=[GitHubFixtureProtocol.self]
        let (client,data)=try await UpdateBrandMigration.latestFeed(repository:UpdateBrandMigration.current,token:fakeToken,configuration:configuration)
        XCTAssertEqual(client.repository,UpdateBrandMigration.previous);XCTAssertEqual(data,feed);XCTAssertEqual(paths.count,3)
    }
    func testRebrandDoesNotFallBackOnAuthorizationFailure() async throws {
        var paths:[String]=[]
        GitHubFixtureProtocol.install { request in paths.append(request.url!.path);return .init(status:403,body:Data()) }
        let configuration=URLSessionConfiguration.ephemeral;configuration.protocolClasses=[GitHubFixtureProtocol.self]
        do { _=try await UpdateBrandMigration.latestFeed(repository:UpdateBrandMigration.current,token:fakeToken,configuration:configuration);XCTFail("Authorization failure must fail closed") }
        catch { XCTAssertEqual(error as? GitHubUpdateTransportError,.forbidden) }
        XCTAssertEqual(paths,["/repos/orkunisitmak/nashi/releases/latest"])
    }

    func testRepositoryValidationAndCodableCannotBypassScope() throws {
        for value in ["Example/Nashi", "https://github.com/Example/Nashi", "https://github.com/Example/Nashi.git/"] {
            let repository = try GitHubRepository(value)
            XCTAssertEqual(repository.slug, "example/nashi")
            XCTAssertEqual(repository.websiteURL.absoluteString, "https://github.com/example/nashi")
            XCTAssertEqual(try JSONDecoder().decode(GitHubRepository.self, from: JSONEncoder().encode(repository)), repository)
        }
        for value in ["example", "example/../other", "example/..", "example/%2fother", "example/repo?token=x",
                      "https://github.com.evil.example/example/repo", "http://github.com/example/repo",
                      "https://user:pass@github.com/example/repo", "https://github.com:8443/example/repo",
                      "https://github.com/example/repo?x=1", "https://github.com/example/repo#fragment"] {
            XCTAssertThrowsError(try GitHubRepository(value), value)
            XCTAssertThrowsError(try JSONDecoder().decode(GitHubRepository.self, from: json(["slug": value])), value)
        }
    }

    func testArchiveScopeRequiresAnExactRepositoryAndPositiveAssetID() throws {
        let client = GitHubUpdateClient(repository: try GitHubRepository("Example/Nashi"), token: nil)
        XCTAssertEqual(try client.archiveReference(for: URL(string: "https://github.com/Example/Nashi/releases/download/release%2F1/Nashi%20Test.zip")!), .release(tag: "release/1", filename: "Nashi Test.zip"))
        XCTAssertEqual(try client.archiveReference(for: URL(string: "https://api.github.com/repos/Example/Nashi/releases/assets/42")!), .asset(42))
        for value in ["https://github.com/example/other/releases/download/v1/Test.zip",
                      "https://github.com.evil.example/example/nashi/releases/download/v1/Test.zip",
                      "https://api.github.com/repos/example/nashi/releases/assets/0",
                      "https://api.github.com/repos/example/nashi/releases/assets/-1",
                      "https://api.github.com/repos/example/nashi/releases/assets/999999999999999999999999",
                      "https://api.github.com/repos/example/nashi/releases/assets/42?token=x",
                      "https://github.com/example/nashi/releases/download/v1/%2Fprivate.zip",
                      "https://github.com/example/nashi/releases/download/v1/Test%0D%0AX-Evil.zip",
                      "http://github.com/example/nashi/releases/download/v1/Test.zip",
                      "https://user@github.com/example/nashi/releases/download/v1/Test.zip",
                      "https://github.com:444/example/nashi/releases/download/v1/Test.zip"] {
            XCTAssertThrowsError(try client.archiveReference(for: URL(string: value)!), value)
        }
    }

    func testCDNRedirectCreatesAFreshRequestWithoutCredentials() throws {
        let url = URL(string: "https://release-assets.githubusercontent.com/asset.zip?temporary=fixture")!
        let request = try GitHubUpdateClient.assetRedirectRequest(to: url)
        XCTAssertEqual(request.url, url)
        XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
        XCTAssertNil(request.value(forHTTPHeaderField: "Cookie"))
        XCTAssertEqual(request.value(forHTTPHeaderField: "Accept"), "application/octet-stream")
        for value in ["http://release-assets.githubusercontent.com/asset.zip",
                      "https://release-assets.githubusercontent.com.evil.example/asset.zip",
                      "https://example.com/asset.zip", "https://api.github.com/repos/other/repo/releases/assets/1",
                      "https://user@objects.githubusercontent.com/asset.zip", "https://objects.githubusercontent.com:8443/asset.zip"] {
            XCTAssertThrowsError(try GitHubUpdateClient.assetRedirectRequest(to: URL(string: value)!), value)
        }
    }

    func testValidationAcceptsAnAccessibleRepositoryWithoutAnyRelease() async throws {
        let client = try client { request in
            XCTAssertEqual(request.url?.absoluteString, "https://api.github.com/repos/example/nashi")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer " + self.fakeToken)
            return .init(body: try self.json(["id": 123]))
        }
        try await client.validateAccess()
    }

    func testLatestAppcastDownloadsExactBytesWithTheBinaryAcceptHeader() async throws {
        let client = try client { request in
            XCTAssertEqual(request.url?.host, "api.github.com")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer " + self.fakeToken)
            if request.url?.path.hasSuffix("/releases/latest") == true {
                XCTAssertEqual(request.value(forHTTPHeaderField: "Accept"), "application/vnd.github+json")
                return .init(body: try self.json(["draft": false, "prerelease": false, "assets": [self.asset(self.feed.count, name: "appcast.xml")]]))
            }
            XCTAssertEqual(request.url?.path, "/repos/example/nashi/releases/assets/42")
            XCTAssertEqual(request.value(forHTTPHeaderField: "Accept"), "application/octet-stream")
            return .init(body: self.feed)
        }
        let downloaded = try await client.fetchLatestAppcast()
        XCTAssertEqual(downloaded, feed, "The transport must preserve every signed byte, including BOM, CRLF and footer.")
    }

    func testAssetRedirectStripsTheTokenBeforeTheCDNRequest() async throws {
        let client = try client { request in
            if request.url?.host == "release-assets.githubusercontent.com" {
                XCTAssertNil(request.value(forHTTPHeaderField: "Authorization"))
                XCTAssertNil(request.value(forHTTPHeaderField: "Cookie"))
                return .init(body: self.feed)
            }
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer " + self.fakeToken)
            if request.url?.path.hasSuffix("/releases/latest") == true {
                return .init(body: try self.json(["assets": [self.asset(self.feed.count, name: "appcast.xml")]]))
            }
            return .init(status: 302, chunks: [], redirect: URL(string: "https://release-assets.githubusercontent.com/fixture.xml?temporary=fixture")!)
        }
        let downloaded = try await client.fetchLatestAppcast()
        XCTAssertEqual(downloaded, feed)
    }

    func testHTTPFailuresAreSanitizedAndDistinguishTokenAccessAndRateLimits() async throws {
        for (status, expected) in [(401, GitHubUpdateTransportError.unauthorized), (403, .forbidden), (404, .notFound), (429, .rateLimited)] {
            let client = try client { _ in .init(status: status, body: Data((self.fakeToken + " private server details").utf8)) }
            do { try await client.validateAccess(); XCTFail("Expected HTTP \(status) to fail") }
            catch {
                XCTAssertEqual(error as? GitHubUpdateTransportError, expected)
                XCTAssertFalse(error.localizedDescription.contains(fakeToken))
                XCTAssertFalse(error.localizedDescription.contains("private server details"))
            }
        }
        let client = try client { _ in .init(status: 403, headers: ["X-RateLimit-Remaining": "0"], body: Data()) }
        do { try await client.validateAccess(); XCTFail("Expected rate limit") }
        catch { XCTAssertEqual(error as? GitHubUpdateTransportError, .rateLimited) }
        let secondary = try self.client { _ in .init(status: 403, headers: ["Retry-After": "60", "X-RateLimit-Remaining": "4998"], body: Data()) }
        do { try await secondary.validateAccess(); XCTFail("Expected secondary rate limit") }
        catch { XCTAssertEqual(error as? GitHubUpdateTransportError, .rateLimited) }
    }

    func testOversizedAppcastIsRejectedBeforeDownloadingTheAsset() async throws {
        let client = try client { request in
            XCTAssertTrue(request.url?.path.hasSuffix("/releases/latest") == true)
            return .init(body: try self.json(["assets": [self.asset(GitHubUpdateClient.metadataLimit + 1, name: "appcast.xml")]]))
        }
        do { _ = try await client.fetchLatestAppcast(); XCTFail("Expected bounded feed failure") }
        catch { XCTAssertEqual(error as? GitHubUpdateTransportError, .tooLarge) }
    }

    func testLoopbackServesTheSignedFeedUnchangedAndRejectsWrongRoutesAndMethods() async throws {
        let client = try client { _ in XCTFail("Serving cached feed bytes must not contact GitHub"); return .init(status: 500, body: Data()) }
        let bridge = GitHubUpdateBridge(client: client, appcast: feed)
        let url = try await bridge.start()
        defer { bridge.stop() }
        XCTAssertEqual(url.host, "127.0.0.1"); XCTAssertNotNil(url.port)
        XCTAssertTrue(url.pathComponents[1].count >= 64)
        let session = session(); defer { session.invalidateAndCancel() }
        let (downloaded, response) = try await session.data(from: url)
        XCTAssertEqual(downloaded, feed)
        XCTAssertEqual((response as? HTTPURLResponse)?.statusCode, 200)
        XCTAssertEqual(response.expectedContentLength, Int64(feed.count))
        let wrong = url.deletingLastPathComponent().deletingLastPathComponent().appendingPathComponent("wrong/appcast.xml")
        let (_, wrongResponse) = try await session.data(from: wrong)
        XCTAssertEqual((wrongResponse as? HTTPURLResponse)?.statusCode, 404)
        var request = URLRequest(url: url); request.httpMethod = "POST"
        let (_, postResponse) = try await session.data(for: request)
        XCTAssertEqual((postResponse as? HTTPURLResponse)?.statusCode, 405)
        request = URLRequest(url: url); request.setValue(String(repeating: "X", count: 9000), forHTTPHeaderField: "X-Large")
        let (_, largeResponse) = try await session.data(for: request)
        XCTAssertEqual((largeResponse as? HTTPURLResponse)?.statusCode, 431)
    }

    func testArchiveStreamsExactBytesLengthAndFilenameThroughTheAdapter() async throws {
        let payload = Data((0..<(2 * 1024 * 1024 + 31)).map { UInt8(truncatingIfNeeded: $0) })
        let client = try client { request in
            XCTAssertEqual(request.value(forHTTPHeaderField: "Authorization"), "Bearer " + self.fakeToken)
            if request.value(forHTTPHeaderField: "Accept") == "application/vnd.github+json" {
                XCTAssertEqual(URLComponents(url: request.url!, resolvingAgainstBaseURL: false)?.percentEncodedPath, "/repos/example/nashi/releases/tags/release%2F1")
                return .init(body: try self.json(["draft": false, "assets": [self.asset(payload.count)]]))
            }
            XCTAssertEqual(request.url?.path, "/repos/example/nashi/releases/assets/42")
            return .init(chunks: stride(from: 0, to: payload.count, by: 32768).map { payload.subdata(in: $0..<min($0 + 32768, payload.count)) })
        }
        let bridge = GitHubUpdateBridge(client: client, appcast: feed)
        _ = try await bridge.start(); defer { bridge.stop() }
        let original = URL(string: "https://github.com/Example/Nashi/releases/download/release%2F1/Nashi%20Test.zip")!
        let local = try bridge.archiveURL(for: original)
        XCTAssertEqual(try bridge.archiveURL(for: original), local)
        XCTAssertEqual(local.host, "127.0.0.1")
        XCTAssertFalse(local.absoluteString.contains(fakeToken))
        let session = session(); defer { session.invalidateAndCancel() }
        let (downloaded, response) = try await session.data(from: local)
        XCTAssertEqual(downloaded, payload)
        XCTAssertEqual(response.expectedContentLength, Int64(payload.count))
        XCTAssertEqual((response as? HTTPURLResponse)?.value(forHTTPHeaderField: "Content-Disposition"), "attachment; filename=\"Nashi Test.zip\"")
        XCTAssertEqual(response.suggestedFilename, "Nashi Test.zip")
    }

    func testStopCancelsTheUpstreamArchiveAndTheLoopbackConnection() async throws {
        let started = expectation(description: "Archive stream started")
        let stopped = expectation(description: "Upstream archive cancelled")
        let client = try client { request in
            if request.value(forHTTPHeaderField: "Accept") == "application/vnd.github+json" {
                return .init(body: try self.json(self.asset(1024 * 1024)))
            }
            return .init(headers: ["Content-Length": "1048576"], chunks: [Data(repeating: 42, count: 16384)],
                         keepOpen: true, onStart: { started.fulfill() }, onStop: { stopped.fulfill() })
        }
        let bridge = GitHubUpdateBridge(client: client, appcast: feed)
        _ = try await bridge.start()
        let url = try bridge.archiveURL(for: URL(string: "https://api.github.com/repos/example/nashi/releases/assets/42")!)
        let session = session(); defer { session.invalidateAndCancel(); bridge.stop() }
        let requestFinished = expectation(description: "Loopback consumer cancelled")
        let download = session.dataTask(with: url) { _, _, error in
            XCTAssertNotNil(error)
            requestFinished.fulfill()
        }
        download.resume()
        await fulfillment(of: [started], timeout: 5)
        bridge.stop()
        await fulfillment(of: [stopped, requestFinished], timeout: 5)
        XCTAssertThrowsError(try bridge.archiveURL(for: URL(string: "https://api.github.com/repos/example/nashi/releases/assets/42")!))
    }

    func testEarlyArchiveEOFDoesNotReturnATruncatedSuccess() async throws {
        let client = try client { request in
            if request.value(forHTTPHeaderField: "Accept") == "application/vnd.github+json" {
                return .init(body: try self.json(self.asset(1024)))
            }
            return .init(body: Data(repeating: 1, count: 512))
        }
        let bridge = GitHubUpdateBridge(client: client, appcast: feed)
        _ = try await bridge.start(); defer { bridge.stop() }
        let url = try bridge.archiveURL(for: URL(string: "https://api.github.com/repos/example/nashi/releases/assets/42")!)
        let session = session(); defer { session.invalidateAndCancel() }
        do { _ = try await session.data(from: url); XCTFail("The advertised archive length was not received.") }
        catch { /* A closed response before Content-Length is complete must fail the consumer. */ }
    }
}

private final class GitHubFixtureProtocol: URLProtocol {
    struct Reply {
        var status: Int
        var headers: [String: String]
        var chunks: [Data]
        var keepOpen: Bool
        var onStart: (() -> Void)?
        var onStop: (() -> Void)?
        var redirect: URL?
        init(status: Int = 200, headers: [String: String] = [:], body: Data) {
            self.init(status: status, headers: headers, chunks: [body])
        }
        init(status: Int = 200, headers: [String: String] = [:], chunks: [Data], keepOpen: Bool = false,
             onStart: (() -> Void)? = nil, onStop: (() -> Void)? = nil, redirect: URL? = nil) {
            self.status = status; self.headers = headers; self.chunks = chunks; self.keepOpen = keepOpen
            self.onStart = onStart; self.onStop = onStop
            self.redirect = redirect
        }
    }
    private static let lock = NSLock()
    private static var handler: ((URLRequest) throws -> Reply)?
    private var onStop: (() -> Void)?
    static func install(_ handler: ((URLRequest) throws -> Reply)?) {
        lock.lock(); defer { lock.unlock() }; Self.handler = handler
    }
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        Self.lock.lock(); let handler = Self.handler; Self.lock.unlock()
        do {
            guard let handler else { throw GitHubUpdateTransportError.network }
            let reply = try handler(request)
            onStop = reply.onStop
            var headers = reply.headers
            if headers["Content-Length"] == nil { headers["Content-Length"] = String(reply.chunks.reduce(0) { $0 + $1.count }) }
            let response = HTTPURLResponse(url: request.url!, statusCode: reply.status, httpVersion: "HTTP/1.1", headerFields: headers)!
            if let redirect = reply.redirect {
                var proposed = URLRequest(url: redirect)
                proposed.allHTTPHeaderFields = request.allHTTPHeaderFields
                client?.urlProtocol(self, wasRedirectedTo: proposed, redirectResponse: response)
                return
            }
            client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            for chunk in reply.chunks { client?.urlProtocol(self, didLoad: chunk) }
            reply.onStart?()
            if !reply.keepOpen { client?.urlProtocolDidFinishLoading(self) }
        } catch { client?.urlProtocol(self, didFailWithError: error) }
    }
    override func stopLoading() { onStop?(); onStop = nil }
}
