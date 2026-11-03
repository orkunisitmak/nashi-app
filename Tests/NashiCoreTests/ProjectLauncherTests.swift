import XCTest
@testable import NashiCore

final class ProjectLauncherTests: XCTestCase {
    private var root: URL!

    override func setUpWithError() throws {
        root = FileManager.default.temporaryDirectory.appendingPathComponent("ProjectLauncherTests-\(UUID().uuidString)")
        try FileManager.default.createDirectory(at: root, withIntermediateDirectories: true)
    }
    override func tearDownWithError() throws { try? FileManager.default.removeItem(at: root) }

    private func project(_ name: String, files: [String: String]) throws -> URL {
        let folder = root.appendingPathComponent(name)
        try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true)
        for (path, contents) in files {
            let file = folder.appendingPathComponent(path)
            try FileManager.default.createDirectory(at: file.deletingLastPathComponent(), withIntermediateDirectories: true)
            try contents.write(to: file, atomically: true, encoding: .utf8)
        }
        return folder
    }
    private func commands(_ folder: URL) -> [String: [[String]]] {
        Dictionary(uniqueKeysWithValues: ProjectLauncher.runCommands(for: folder).compactMap { launch in
            if case .terminal(let commands) = launch.kind { return (launch.title, commands) }
            return nil
        })
    }

    func testOnlyASingleFolderIsAProject() throws {
        let folder = try project("app", files: ["README.md": "x"])
        let file = folder.appendingPathComponent("README.md")
        XCTAssertEqual(ProjectLauncher.projectFolder(in: [folder]), folder)
        XCTAssertNil(ProjectLauncher.projectFolder(in: [file]))
        XCTAssertNil(ProjectLauncher.projectFolder(in: [folder, folder]))
        XCTAssertNil(ProjectLauncher.projectFolder(in: [URL(string: "https://example.com")!]))
        XCTAssertNil(ProjectLauncher.projectFolder(in: []))
    }

    func testEditorAppearsOnlyWhenInstalledAndClaudeCodeIsAlwaysOffered() throws {
        let folder = try project("plain", files: [:])
        let without = ProjectLauncher.launches(for: folder) { _ in false }
        XCTAssertEqual(without.map(\.title), ["Claude Code"])
        XCTAssertEqual(without.first?.kind, .terminal(commands: [["claude"]]))
        let with = ProjectLauncher.launches(for: folder) { $0 == ProjectLauncher.visualStudioCode }
        XCTAssertEqual(with.map(\.title), ["VS Code", "Claude Code"])
        XCTAssertEqual(with.first?.kind, .openWith(bundleIdentifier: ProjectLauncher.visualStudioCode))
    }

    func testNodeScriptsInstallFirstOnlyWhenDependenciesAreMissing() throws {
        let manifest = #"{"scripts":{"dev":"vite","start":"node server.js","build":"vite build"}}"#
        let fresh = try project("fresh", files: ["package.json": manifest])
        XCTAssertEqual(commands(fresh), [
            "npm run dev": [["npm", "install"], ["npm", "run", "dev"]],
            "npm run start": [["npm", "install"], ["npm", "run", "start"]]
        ])
        let installed = try project("installed", files: ["package.json": manifest, "node_modules/.keep": ""])
        XCTAssertEqual(commands(installed)["npm run dev"], [["npm", "run", "dev"]])
        XCTAssertEqual(ProjectLauncher.runCommands(for: installed).map(\.title), ["npm run dev", "npm run start"])
    }

    func testPackageManagerFollowsCorepackFieldThenLockFile() throws {
        let scripts = #""scripts":{"dev":"next dev"}"#
        let pnpm = try project("pnpm", files: ["package.json": "{\(scripts)}", "pnpm-lock.yaml": "", "node_modules/.keep": ""])
        let yarn = try project("yarn", files: ["package.json": "{\(scripts)}", "yarn.lock": "", "node_modules/.keep": ""])
        let bun = try project("bun", files: ["package.json": "{\(scripts)}", "bun.lock": "", "node_modules/.keep": ""])
        let declared = try project("declared", files: ["package.json": #"{"packageManager":"pnpm@9.12.0",\#(scripts)}"#, "yarn.lock": "", "node_modules/.keep": ""])
        let unknown = try project("unknown", files: ["package.json": #"{"packageManager":"rm -rf@1",\#(scripts)}"#, "node_modules/.keep": ""])
        XCTAssertEqual(commands(pnpm)["pnpm run dev"], [["pnpm", "run", "dev"]])
        XCTAssertEqual(commands(yarn)["yarn run dev"], [["yarn", "run", "dev"]])
        XCTAssertEqual(commands(bun)["bun run dev"], [["bun", "run", "dev"]])
        XCTAssertEqual(commands(declared)["pnpm run dev"], [["pnpm", "run", "dev"]])
        XCTAssertEqual(commands(unknown)["npm run dev"], [["npm", "run", "dev"]])
    }

    func testOtherToolchainsAndBrokenManifests() throws {
        let flutter = try project("flutter", files: ["pubspec.yaml": "dependencies:\n  flutter:\n    sdk: flutter\n"])
        let dart = try project("dart", files: ["pubspec.yaml": "name: tool\n"])
        let rust = try project("rust", files: ["Cargo.toml": "[package]"])
        let go = try project("go", files: ["go.mod": "module x"])
        let swift = try project("swift", files: ["Package.swift": "// swift-tools-version: 6.0"])
        let broken = try project("broken", files: ["package.json": "{ not json"])
        let noScripts = try project("library", files: ["package.json": #"{"name":"lib"}"#])
        XCTAssertEqual(commands(flutter), ["flutter run": [["flutter", "run"]]])
        XCTAssertEqual(commands(dart), ["dart run": [["dart", "run"]]])
        XCTAssertEqual(commands(rust), ["cargo run": [["cargo", "run"]]])
        XCTAssertEqual(commands(go), ["go run .": [["go", "run", "."]]])
        XCTAssertEqual(commands(swift), ["swift run": [["swift", "run"]]])
        XCTAssertTrue(commands(broken).isEmpty)
        XCTAssertTrue(commands(noScripts).isEmpty)
    }

    func testShellQuotingKeepsEveryWordLiteral() {
        XCTAssertEqual(ProjectLauncher.shellQuoted("plain"), "'plain'")
        XCTAssertEqual(ProjectLauncher.shellQuoted("it's"), #"'it'\''s'"#)
        XCTAssertEqual(ProjectLauncher.shellQuoted("$(id) `id` ; &&"), "'$(id) `id` ; &&'")
    }

    /// Runs the generated script in a real shell inside a hostile folder name.
    func testScriptRunsInsideTheFolderWithoutInterpretingItsName() throws {
        let folder = try project("it's $(touch pwned) `touch pwned2`; \"x\"", files: [:])
        let script = root.appendingPathComponent("launch.command")
        let body = ProjectLauncher.terminalScript(commands: [["/usr/bin/touch", "first marker"], ["/usr/bin/touch", "second"]], in: folder)
        try body.write(to: script, atomically: true, encoding: .utf8)
        try FileManager.default.setAttributes([.posixPermissions: 0o700], ofItemAtPath: script.path)

        let process = Process()
        process.executableURL = script
        process.currentDirectoryURL = root
        process.environment = ["SHELL": "/bin/sh", "PATH": "/usr/bin:/bin", "HOME": root.path]
        process.standardInput = FileHandle.nullDevice
        process.standardOutput = FileHandle.nullDevice
        process.standardError = FileHandle.nullDevice
        try process.run()
        process.waitUntilExit()

        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("first marker").path))
        XCTAssertTrue(FileManager.default.fileExists(atPath: folder.appendingPathComponent("second").path))
        XCTAssertFalse(FileManager.default.fileExists(atPath: script.path), "The script should delete itself")
        let leaked = try FileManager.default.contentsOfDirectory(atPath: root.path).filter { $0.hasPrefix("pwned") }
        XCTAssertEqual(leaked, [])
        XCTAssertEqual(try FileManager.default.contentsOfDirectory(atPath: folder.path).filter { $0.hasPrefix("pwned") }, [])
    }
}
