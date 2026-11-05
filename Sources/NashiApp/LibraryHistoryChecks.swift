import Foundation
import NashiCore

@MainActor enum LibraryHistoryChecks {
    static func run()throws->[String] {
        let root=try OutputFiles.temporaryDirectory();defer { try? FileManager.default.removeItem(at:root) }
        let file=root.appendingPathComponent("Original.txt");try Data("Keep this file".utf8).write(to:file)
        let suite="nashi-history-checks-"+UUID().uuidString,defaults=UserDefaults(suiteName:suite)!
        defer { defaults.removePersistentDomain(forName:suite) }
        defaults.set("dark",forKey:"appAppearance")
        let first=JobRecord(title:"First",inputs:[file.path],outputs:[file.path]),second=JobRecord(title:"Second",inputs:[],outputs:[file.path])
        defaults.set(try JSONEncoder().encode([first,second]),forKey:"history")
        let state=AppState(defaults:defaults);state.selectedHistoryID=first.id
        state.removeHistory(first.id)
        guard state.selectedHistoryID==nil,AppState(defaults:defaults).history.map(\.id)==[second.id] else { throw UIAuditError("Removing history was not persisted or kept stale selection") }
        state.working=true;state.clearHistory();guard state.history.count==1 else { throw UIAuditError("Active job allowed history clearing") };state.working=false
        state.clearHistory()
        guard AppState(defaults:defaults).history.isEmpty,defaults.string(forKey:"appAppearance")=="dark",try Data(contentsOf:file)==Data("Keep this file".utf8) else { throw UIAuditError("Clear history removed files, settings or failed to persist") }
        return ["History removal persists and clears selection","Active job preserves history","Clear history persists without deleting files or settings"]
    }
}
