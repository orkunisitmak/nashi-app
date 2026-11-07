import AppKit
import SwiftUI
import NashiCore

struct ImageExportVariantDraft: Identifiable {
    let id = UUID()
    var format: String
    var edge: String
    var subfolder: String
    init(_ value: ImageExportVariant) { format = value.format; edge = String(value.maxDimension); subfolder = value.subfolder }
    var value: ImageExportVariant { .init(format: format, maxDimension: Int(edge) ?? -1, subfolder: subfolder) }
}

@MainActor final class ImageExportPackageEditorModel: ObservableObject {
    @Published var name = ImageExportPackage.web.name
    @Published var variants = ImageExportPackage.web.variants.map(ImageExportVariantDraft.init)
    @Published var selectedSaved = ""
    @Published private(set) var saved: [ImageExportPackage]
    @Published var message: String?
    private let store: ImageExportPackageStore
    init(store: ImageExportPackageStore = .init()) { self.store = store; saved = store.packages }
    var package: ImageExportPackage { .init(name: name, variants: variants.map(\.value)) }
    func load(_ name: String) {
        let value = saved.first { $0.name == name } ?? .web
        guard package != value else { return }
        self.name = value.name; variants = value.variants.map(ImageExportVariantDraft.init); message = nil
    }
    func save() {
        do { try store.save(package); saved = store.packages; selectedSaved = name; message = "Package saved." }
        catch { message = error.localizedDescription }
    }
    func remove() {
        do { try store.remove(named: selectedSaved); saved = store.packages; selectedSaved = ""; message = "Saved package removed." }
        catch { message = error.localizedDescription }
    }
}

struct ImageExportPackageView: View {
    @ObservedObject var state: AppState
    let inputs: [URL]
    @StateObject private var model: ImageExportPackageEditorModel
    @State private var jobToken: CancellationToken?
    private let chooseDestination: (() -> URL?)?

    @MainActor init(state: AppState, inputs: [URL], model: ImageExportPackageEditorModel? = nil, chooseDestination: (() -> URL?)? = nil) {
        self.state = state; self.inputs = inputs; self.chooseDestination = chooseDestination
        _model = StateObject(wrappedValue: model ?? ImageExportPackageEditorModel())
    }
    private var plan: [ImageExportOutput] { (try? model.package.plan(inputs: inputs)) ?? [] }
    private var validation: String? {
        do { _ = try model.package.plan(inputs: inputs); return nil } catch { return error.localizedDescription }
    }

    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 12) {
                WindowCloseButton().frame(width: 26, height: 26)
                ZStack {
                    WindowDragRegion()
                    HStack(spacing: 12) {
                        Image(systemName: "square.stack.3d.up").foregroundStyle(Color.nashi).font(.title2)
                        VStack(alignment: .leading, spacing: 4) {
                            Text("Export image package").font(.system(size: 21, weight: .bold, design: .rounded))
                            Text("Several formats and sizes, in one export.").font(.caption).foregroundStyle(.secondary)
                        }
                        Spacer()
                    }.allowsHitTesting(false)
                }
            }.padding(20)
            Divider()
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    HStack {
                        Picker("Saved packages", selection: $model.selectedSaved) {
                            Text("Example package").tag("")
                            ForEach(model.saved, id: \.name) { Text(verbatim: $0.name).tag($0.name) }
                        }.accessibilityIdentifier("package.saved")
                        Button("Delete saved package") { model.remove() }.disabled(model.selectedSaved.isEmpty)
                    }.onChange(of: model.selectedSaved) { model.load($0) }
                    HStack {
                        Text("Package name")
                        TextField("Package name", text: $model.name).accessibilityIdentifier("package.name")
                    }
                    Text("Each output gets its own subfolder. Size limits keep the aspect ratio and never enlarge images.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    HStack {
                        Text("Format").frame(width: 100, alignment: .leading)
                        Text("Longest edge (px)").frame(width: 145, alignment: .leading)
                        Text("Subfolder"); Spacer()
                    }.font(.caption).foregroundStyle(.secondary)
                    ForEach($model.variants) { $row in
                        HStack(spacing: 10) {
                            Picker("Format", selection: $row.format) {
                                ForEach(ImageExportPackage.formats, id: \.self) { Text(verbatim: $0.uppercased()).tag($0) }
                            }.labelsHidden().frame(width: 100).accessibilityIdentifier("package.format.\(row.id)")
                            TextField("Longest edge (px)", text: $row.edge).frame(width: 145)
                            TextField("Subfolder", text: $row.subfolder)
                            Button { model.variants.removeAll { $0.id == row.id } } label: { Image(systemName: "minus.circle") }
                                .accessibilityLabel("Remove output").disabled(model.variants.count == 1)
                        }
                    }
                    HStack {
                        Button { model.variants.append(.init(.init(format: "png", maxDimension: 0, subfolder: "output\(model.variants.count + 1)"))) }
                            label: { Label("Add output", systemImage: "plus") }.disabled(model.variants.count >= 12)
                        Text("0 keeps the original size.").font(.caption).foregroundStyle(.secondary)
                        Spacer()
                        Button("Save package") { model.save() }.disabled(validation != nil).accessibilityIdentifier("package.save")
                    }
                    if let validation { LText(validation).font(.callout).foregroundStyle(.orange) }
                    if let message = model.message { LText(message).font(.caption).accessibilityIdentifier("package.message") }
                    Divider()
                    LText(plan.count == 1 ? "1 output file" : "\(plan.count) output files").font(.headline).accessibilityIdentifier("package.count")
                    Text("A new package folder keeps existing files safe. Its name gets a number if needed.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                    VStack(alignment: .leading, spacing: 4) {
                        Text(verbatim: model.name + "/").fontWeight(.semibold)
                        ForEach(Array(plan.prefix(24).enumerated()), id: \.offset) { _, entry in
                            Text(verbatim: "  " + entry.relativePath).lineLimit(1).truncationMode(.middle)
                        }
                        if plan.count > 24 { LText("\(plan.count - 24) more outputs") }
                    }.font(.system(size: 11, design: .monospaced)).frame(maxWidth: .infinity, alignment: .leading)
                        .padding(14).background(NashiPalette.inset, in: RoundedRectangle(cornerRadius: 12)).accessibilityIdentifier("package.preview")
                    Text("Source metadata is omitted. JPG uses a white background for transparent areas.")
                        .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
                }.padding(22)
            }.disabled(state.working)
            if let jobToken, state.token === jobToken { JobProgressView(state: state).padding(.horizontal, 18).padding(.vertical, 8) }
            Divider()
            HStack {
                Label("Original stays safe", systemImage: "lock").font(.caption).foregroundStyle(.secondary)
                Spacer()
                Button("Export package…") { export() }.buttonStyle(NashiButtonStyle(.primary))
                    .keyboardShortcut("s", modifiers: .command).disabled(validation != nil || state.working).accessibilityIdentifier("package.export")
            }.padding(18)
        }.frame(minWidth: 700, minHeight: 560).background(NashiPalette.canvas).tint(.nashi)
            .buttonStyle(NashiButtonStyle(.secondary, compact: true)).textFieldStyle(.roundedBorder)
    }

    private func export() {
        let destination: URL?
        if let chooseDestination { destination = chooseDestination() }
        else {
            let panel = NSOpenPanel(); panel.canChooseFiles = false; panel.canChooseDirectories = true; panel.canCreateDirectories = true
            panel.prompt = L10n.text("Export Here"); panel.directoryURL = inputs.first?.deletingLastPathComponent()
            destination = panel.runModal() == .OK ? panel.url : nil
        }
        guard let destination else { return }
        var options = OperationOptions(); options.outputDirectory = destination
        state.execute(.imageExportPackage(model.package), options: options, inputs: inputs)
        jobToken = state.token
    }
}
