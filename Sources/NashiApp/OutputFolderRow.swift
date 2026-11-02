import AppKit
import SwiftUI

/// "Save copies" row used by Settings › General.
struct OutputFolderRow:View {
    @State private var folder:URL?
    @State private var missing=false

    private var selection:Binding<String> {
        Binding(get:{ folder == nil ? "beside":"folder" },set:{ value in
            guard value == "folder" else { OutputPreferences.clear();refresh();return }
            if OutputPreferences.choose() { refresh() }
        })
    }

    var body:some View {
        VStack(alignment:.leading,spacing:7) {
            HStack(spacing:12) {
                Text("Save copies")
                Spacer(minLength:8)
                Picker("Save copies",selection:selection) {
                    Text("Beside the original").tag("beside")
                    Text("In a folder").tag("folder")
                }.labelsHidden().pickerStyle(.segmented).frame(width:230)
            }
            if let folder {
                HStack(spacing:8) {
                    Image(systemName:"folder").font(.caption).foregroundStyle(Color.nashi)
                    Text(folder.path).font(.caption).foregroundStyle(.secondary).lineLimit(1).truncationMode(.middle).textSelection(.enabled)
                    Spacer(minLength:0)
                    Button("Change Folder…") { if OutputPreferences.choose() { refresh() } }
                        .buttonStyle(.plain).font(.caption.weight(.medium)).foregroundStyle(Color.nashi)
                }
            }
            if missing {
                Text("The chosen folder is no longer available. Results go beside the originals until you choose another.")
                    .font(.caption).foregroundStyle(Color.orange)
            } else if folder != nil {
                Text("New files are written here instead of beside the originals.").font(.caption).foregroundStyle(.secondary)
            } else {
                Text("Results go beside the original file unless you choose a folder.").font(.caption).foregroundStyle(.secondary)
            }
        }
        .onAppear(perform:refresh)
        .onReceive(NotificationCenter.default.publisher(for:NSApplication.didBecomeActiveNotification)) { _ in refresh() }
    }

    private func refresh() {
        folder=OutputPreferences.folder
        missing=OutputPreferences.isMissing
    }
}
