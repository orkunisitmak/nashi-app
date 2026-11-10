// swift-tools-version:5.5
import Foundation
import AppKit
import SwiftUI

@MainActor
public final class AppState: ObservableObject {
    // Published state
    @Published public var working: Bool = false
    @Published public var inputs: [URL] = []
    @Published public var error: String?
    
    // History management
    @Published public private(set) var history: [JobRecord] = []
    @Published public private(set) var selectedHistoryID: UUID?
    
    // Closures
    public var onWebDownload: ((URL?, WebDownloadMode) -> Void)?
    public var receiveProgress: ((String, Double) -> Void)?
    
    // UserDefaults keys
    private enum StorageKey: String {
        case history = "history"
    }
    
    // FileAction enum - needed for Wheel.swift .actions mode
    public enum FileAction: String, CaseIterable, Sendable {
        case convert
        case tool
        case actions
        
        public var title: String {
            switch self {
            case .convert: return "Convert"
            case .tool: return "Tools"
            case .actions: return "Actions"
            }
        }
        
        public var symbol: String {
            switch self {
            case .convert: return "arrow.triangle.2.circlepath"
            case .tool: return "hammer"
            case .actions: return "paperplane"
            }
        }
    }
    
    // JobRecord struct
    public struct JobRecord: Identifiable, Sendable {
        public let id = UUID()
        public let title: String
        public let outputs: [URL]
        public let mode: WheelMode
        public let created: Date
        
        public init(title: String, outputs: [URL], mode: WheelMode) {
            self.title = title
            self.outputs = outputs
            self.mode = mode
            self.created = Date()
        }
    }
    
    // History storage
    private let defaults = UserDefaults.standard
    
    public init() {
        loadHistory()
    }
    
    // MARK: - History Management
    
    public func addHistory(_ record: JobRecord) {
        history.append(record)
        saveHistory()
    }
    
    public func removeHistoryEntry(_ id: UUID) {
        if let index = history.firstIndex(where: { $0.id == id }) {
            history.remove(at: index)
            // If the removed entry was selected, select the first remaining entry or clear selection
            if selectedHistoryID == id {
                if !history.isEmpty {
                    selectedHistoryID = history[0].id
                } else {
                    selectedHistoryID = nil
                }
            }
        }
        saveHistory()
    }
    
    public func clearHistory() {
        history.removeAll()
        selectedHistoryID = nil
        saveHistory()
    }
    
    // MARK: - Progress
    
    public func progress(_ label: String, _ progress: Double) {
        receiveProgress?(label, progress)
    }
    
    // MARK: - Web Download
    
    public func webDownload(_ url: URL?, mode: WebDownloadMode) {
        onWebDownload?(url, mode)
    }
    
    // MARK: - Private Storage
    
    private func saveHistory() {
        do {
            let data = try JSONEncoder().encode(history)
            defaults.set(data, forKey: StorageKey.history.rawValue)
        } catch {
            print("Failed to save history: \(error)")
        }
    }
    
    private func loadHistory() {
        if let data = defaults.data(forKey: StorageKey.history.rawValue),
           let decoded = try? JSONDecoder().decode([JobRecord].self, from: data) {
            history = decoded
        }
    }
}
