// Swift compile errors fix for macOS build
// Added missing members for Wheel.swift and WebDownloadView.swift compatibility

var working: Bool
var onWebDownload: ((URL?, WebDownloadMode) -> Void)?
var inputs: [URL] = []
var receiveProgress: ((Double) -> Void)?

