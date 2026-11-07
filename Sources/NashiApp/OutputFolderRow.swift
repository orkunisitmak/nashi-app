import AppKit
import SwiftUI

/// "Save copies" row used by Settings › General.
///
/// The saved path, not the resolved folder, decides which segment is selected:
/// an unmounted drive must still show as "In a folder" with its path and an
/// orange warning, so the person can either reconnect it or switch back with
/// one click.
struct OutputFolderRow:View {
    @State private var savedPath=""
    @State private var missing=false
    @State private var saveError=false

    private var usesFolder:Bool { !savedPath.isEmpty }

    private var selection:Binding<String> {
        Binding(get:{ usesFolder ? "folder":"beside" },set:{ value in
            guard value == "folder" else { OutputPreferences.clear();refresh();return }
            choose()
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
            if usesFolder {
                HStack(spacing:8) {
                    Image(systemName:"folder").font(.caption).foregroundStyle(missing ? Color.orange:Color.nashi)
                    Text(savedPath).font(.caption).foregroundStyle(missing ? Color.orange:Color.secondary)
                        .lineLimit(1).truncationMode(.middle).textSelection(.enabled)
                    Spacer(minLength:0)
                    Button("Change Folder…") { choose() }
                        .buttonStyle(.plain).font(.caption.weight(.medium)).foregroundStyle(Color.nashi)
                }
            }
            if saveError {
                Text("The folder could not be saved. Choose another folder.")
                    .font(.caption).foregroundStyle(Color.orange)
            } else if missing {
                Text("The chosen folder is unavailable right now. Results go beside the originals and return to this folder when it is available again.")
                    .font(.caption).foregroundStyle(Color.orange)
            } else if usesFolder {
                Text("New files are written here instead of beside the originals.").font(.caption).foregroundStyle(.secondary)
            } else {
                Text("Results go beside the original file unless you choose a folder.").font(.caption).foregroundStyle(.secondary)
            }
        }
        .onAppear(perform:refresh)
        .onReceive(NotificationCenter.default.publisher(for:NSApplication.didBecomeActiveNotification)) { _ in refresh() }
    }

    private func choose() {
        do { if try OutputPreferences.choose() { saveError=false } }
        catch { saveError=true }
        refresh()
    }

    private func refresh() {
        savedPath=OutputPreferences.savedPath
        missing=OutputPreferences.isMissing
    }
}
