import AppKit
import SwiftUI
import NashiCore
import NashiUpdates

/// Generated fixtures and an isolated defaults suite. No clipboard, hooks or updater.
@MainActor final class ImageExportPackageSmoke: NSObject, NSApplicationDelegate {
    private let output: URL
    private var window: NSWindow?
    private var checks: [String] = []
    init(output: URL) { self.output = output }
    func applicationDidFinishLaunching(_ notification: Notification) {
        Task {
            var failure: String?
            do { try await run() } catch { failure = String(describing: error) }
            let report: [String: Any] = ["passed": failure == nil, "checks": checks, "error": failure as Any? ?? NSNull(),
                "scope": "Own-window package controls, isolated package persistence, PNG/JPG export through AppState, Library recording and EN/TR/FR light/dark renders."]
            if FileManager.default.fileExists(atPath: output.appendingPathComponent(".nashi-package-smoke").path),
               let data = try? JSONSerialization.data(withJSONObject: report, options: [.prettyPrinted, .sortedKeys]) {
                try? data.write(to: output.appendingPathComponent("mac-image-packages.json"))
            }
            if let failure { fputs(failure + "\n", stderr) }
            window?.close(); exit(failure == nil ? 0 : 1)
        }
    }
    private func check(_ condition: Bool, _ message: String) throws {
        guard condition else { throw NSError(domain: "Nashi Package QA", code: 1, userInfo: [NSLocalizedDescriptionKey: message]) }
        checks.append(message)
    }
    private func pause() async { try? await Task.sleep(nanoseconds: 180_000_000) }
    private func attribute(_ object: NSObject, _ name: String) -> Any? {
        let selector = NSSelectorFromString(name)
        return object.responds(to: selector) ? object.perform(selector)?.takeUnretainedValue() : nil
    }
    private func elements(_ object: NSObject) -> [NSObject] {
        var seen = Set<ObjectIdentifier>()
        func walk(_ item: NSObject, depth: Int) -> [NSObject] {
            guard depth < 40, seen.insert(ObjectIdentifier(item)).inserted else { return [] }
            let children = (attribute(item, "accessibilityChildren") as? [NSObject] ?? []) + ((item as? NSView)?.subviews ?? [])
            return [item] + children.flatMap { walk($0, depth: depth + 1) }
        }
        return walk(object, depth: 0)
    }
    private func press(_ identifier: String) throws {
        guard let root = window?.contentView, let element = elements(root).first(where: { attribute($0, "accessibilityIdentifier") as? String == identifier }) else {
            if let root = window?.contentView {
                let trace = elements(root).map { [attribute($0, "accessibilityIdentifier"), attribute($0, "accessibilityLabel"), attribute($0, "accessibilityValue")].map { String(describing: $0) }.joined(separator: " · ") }.joined(separator: "\n")
                try? trace.write(to: output.appendingPathComponent("controls.txt"), atomically: true, encoding: .utf8)
                try? capture("control-not-found.png")
            }
            throw NSError(domain: "Nashi Package QA", code: 2, userInfo: [NSLocalizedDescriptionKey: "Missing package control: " + identifier])
        }
        let selector = NSSelectorFromString("accessibilityPerformPress")
        try check(element.responds(to: selector), "Production control has a press action: " + identifier)
        // SwiftUI accessibility nodes implement the ObjC selectors without declaring
        // NSAccessibilityProtocol conformance. The documented press selector returns BOOL.
        let action = unsafeBitCast(element.method(for: selector), to: (@convention(c) (AnyObject, Selector) -> Bool).self)
        try check(action(element, selector), "Production control pressed: " + identifier)
    }
    private func capture(_ name: String) throws {
        let view = try unwrap(window?.contentView)
        view.layoutSubtreeIfNeeded()
        let rep = try unwrap(view.bitmapImageRepForCachingDisplay(in: view.bounds))
        view.cacheDisplay(in: view.bounds, to: rep)
        let data = try unwrap(rep.representation(using: .png, properties: [:]))
        try check(data.count > 1000, "Nonblank render: " + name)
        try data.write(to: output.appendingPathComponent(name))
    }
    private func unwrap<T>(_ value: T?) throws -> T {
        guard let value else { throw NSError(domain: "Nashi Package QA", code: 3) }; return value
    }
    private func run() async throws {
        let manager = FileManager.default, marker = output.appendingPathComponent(".nashi-package-smoke")
        if manager.fileExists(atPath: output.path), !manager.fileExists(atPath: marker.path), !(try manager.contentsOfDirectory(atPath: output.path)).isEmpty {
            throw NSError(domain: "Nashi Package QA", code: 4, userInfo: [NSLocalizedDescriptionKey: "Choose an empty package QA directory."])
        }
        try manager.createDirectory(at: output, withIntermediateDirectories: true)
        try Data("Generated image package QA\n".utf8).write(to: marker)
        let suite = "Nashi.PackageQA." + UUID().uuidString, defaults = try unwrap(UserDefaults(suiteName: suite))
        let originalArguments = UserDefaults.standard.volatileDomain(forName: UserDefaults.argumentDomain)
        defer { defaults.removePersistentDomain(forName: suite); UserDefaults.standard.setVolatileDomain(originalArguments, forName: UserDefaults.argumentDomain) }
        let source = output.appendingPathComponent("İstanbul 🍐 Home Library.png")
        let context = try unwrap(CGContext(data: nil, width: 80, height: 40, bitsPerComponent: 8, bytesPerRow: 0,
            space: CGColorSpace(name: CGColorSpace.sRGB)!, bitmapInfo: CGImageAlphaInfo.premultipliedLast.rawValue))
        context.setFillColor(CGColor(red: 0.2, green: 0.5, blue: 0.3, alpha: 0.5)); context.fill(CGRect(x: 0, y: 0, width: 80, height: 40))
        try ImageEngine.write(try unwrap(context.makeImage()), to: source)
        let original = try Data(contentsOf: source), store = ImageExportPackageStore(defaults: defaults)
        let model = ImageExportPackageEditorModel(store: store), state = AppState(defaults: defaults)
        state.inputs = [source]
        let destination = output.appendingPathComponent("Exports")
        var allowExport = false
        let host = NSWindow(contentRect: NSRect(x: 100, y: 80, width: 740, height: 1100), styleMask: [.titled, .closable, .resizable], backing: .buffered, defer: false)
        host.isReleasedWhenClosed = false; window = host
        defaults.set("en", forKey: "appLanguage")
        var opened = 0
        state.onImageExportPackage = { opened += 1 }
        host.contentView = NSHostingView(rootView: HomeView(state: state, updater: AppUpdater()).nashiLanguage().defaultAppStorage(defaults))
        host.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); host.contentView?.layoutSubtreeIfNeeded(); await pause(); await pause(); await pause()
        try press("home.export-package")
        try check(opened == 1, "Home exposes the package action for images")
        let text = output.appendingPathComponent("Note.txt"); try Data("Synthetic fixture".utf8).write(to: text)
        state.inputs = [source, text]; await pause()
        try check(!elements(try unwrap(host.contentView)).contains { attribute($0, "accessibilityIdentifier") as? String == "home.export-package" }, "Mixed image/document selections do not offer image packages")
        state.inputs = [source]
        host.setContentSize(NSSize(width: 740, height: 720))
        for appearance in ["light", "dark"] { for language in ["en", "tr", "fr"] {
            defaults.set(language, forKey: "appLanguage")
            var domain = originalArguments; domain["appLanguage"] = language
            UserDefaults.standard.setVolatileDomain(domain, forName: UserDefaults.argumentDomain)
            host.appearance = NSAppearance(named: appearance == "dark" ? .darkAqua : .aqua)
            host.contentView = NSHostingView(rootView: ImageExportPackageView(state: state, inputs: [source], model: model, chooseDestination: { allowExport ? destination : nil })
                .nashiLanguage().defaultAppStorage(defaults).environment(\.colorScheme, appearance == "dark" ? .dark : .light))
            host.makeKeyAndOrderFront(nil); NSApp.activate(ignoringOtherApps: true); await pause(); await pause()
            try check(try model.package.plan(inputs: [source]).count == 3, "Example package previews three outputs: " + language)
            try capture("mac-image-package-\(language)-\(appearance).png")
        }}
        model.name = "Ürün fotoğrafları"
        model.variants = [.init(.init(format: "png", maxDimension: 40, subfolder: "web")), .init(.init(format: "jpg", maxDimension: 16, subfolder: "thumbnail"))]
        await pause(); try press("package.save"); await pause()
        try check(store.packages == [model.package], "Save button persists the edited package in the isolated store")
        let reopened = ImageExportPackageEditorModel(store: store)
        reopened.load(model.name)
        try check(reopened.package == model.package, "Reopening restores formats, size limits and subfolders")
        model.variants[0].edge = "not a number"; await pause()
        try check((try? model.package.plan(inputs: [source])) == nil, "Invalid numeric text cannot export using a stale size")
        try check(!state.working && state.outputs.isEmpty, "Editing package settings has no file side effects")
        model.variants[0].edge = "40"; await pause()
        try press("package.export"); await pause()
        try check(!state.working && state.outputs.isEmpty && state.history.isEmpty, "Cancelling destination selection does not start a job")
        allowExport = true
        try press("package.export")
        let deadline = Date().addingTimeInterval(10)
        while (state.working || state.outputs.isEmpty) && Date() < deadline { await pause() }
        try check(!state.working && state.error == nil && state.outputs.count == 2, "Export button runs the real AppState transaction")
        let image = try ImageEngine.load(state.outputs[0])
        try check(image.width == 40 && image.height == 20, "UI export applies the selected longest edge")
        try check(state.history.first?.outputs == state.outputs.map(\.path), "All package files are recorded in one Library job")
        try check(try Data(contentsOf: source) == original, "UI export preserves original bytes")
        try store.remove(named: model.name)
        try check(store.packages.isEmpty && state.outputs.allSatisfy { manager.fileExists(atPath: $0.path) }, "Deleting a saved package keeps its exported files")
    }
}
