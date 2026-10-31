import SwiftUI
import NashiUpdates

extension Notification.Name {
    static let showUpdateSettings=Notification.Name("NashiShowUpdateSettings")
    static let showUpdateConnection=Notification.Name("NashiShowUpdateConnection")
}

struct UpdateBanner:View {
    @ObservedObject var updater:AppUpdater
    let version:String
    var body:some View {
        HStack(spacing:13) {
            Image(systemName:"arrow.down.circle.fill").font(.system(size:27)).foregroundStyle(NashiPalette.forest)
            VStack(alignment:.leading,spacing:4) {
                Text("A fresh Nashi is ready.").font(.system(size:13,weight:.bold,design:.rounded))
                Text(updater.waitingForCurrentTask ? "Your task will finish before Nashi restarts.":"Version \(version) · Install when you’re ready.").font(.caption).foregroundStyle(.secondary)
            }
            Spacer(minLength:12)
            Button("Update…") { updater.checkForUpdates() }
                .buttonStyle(NashiButtonStyle(.secondary,compact:true))
                .disabled(!updater.canCheckForUpdates || updater.isBusy || updater.waitingForCurrentTask)
        }.padding(17).background(NashiPalette.sage,in:RoundedRectangle(cornerRadius:18))
        .accessibilityElement(children:.contain)
    }
}

struct UpdateSettingsView:View {
    @ObservedObject var updater:AppUpdater
    private var version:String { Bundle.main.object(forInfoDictionaryKey:"CFBundleShortVersionString") as? String ?? "Development" }
    private var build:String { Bundle.main.object(forInfoDictionaryKey:"CFBundleVersion") as? String ?? "—" }

    var body:some View {
        ScrollView {
            VStack(alignment:.leading,spacing:22) {
                NashiPageHeading(eyebrow:"Updates",title:"Always a little fresher.",subtitle:"New features and small improvements, delivered right here.")
                HStack(spacing:17) {
                    NashiMark(size:62,happy:updater.availableVersion != nil)
                    VStack(alignment:.leading,spacing:6) {
                        Text("Nashi \(version)").font(.system(size:21,weight:.bold,design:.rounded))
                        Text("Build \(build)").font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    if updater.availableVersion != nil { NashiPill(title:"Update ready",symbol:"sparkle",color:NashiPalette.forest) }
                }.padding(22).nashiCard()
                HStack(spacing:12) {
                    Image(systemName:"externaldrive.badge.person.crop").foregroundStyle(NashiPalette.forest)
                    VStack(alignment:.leading,spacing:4) {
                        Group { if let slug=updater.repository?.slug ?? updater.officialRepository?.slug { Text(verbatim:slug) } else { LText("GitHub release connection") } }.font(.headline)
                        Text(updater.repository == nil ? (updater.officialRepository == nil ? "Connect a private or public updates repository.":"Official public releases · No account or token needed"):updater.requiresToken ? "Private repository · Token saved in Keychain":"Public repository · No token needed")
                            .font(.caption).foregroundStyle(.secondary)
                    }
                    Spacer()
                    if let official=updater.officialRepository,updater.repository == nil {
                        Link("View releases",destination:official.websiteURL.appendingPathComponent("releases"))
                            .buttonStyle(NashiButtonStyle(.secondary,compact:true))
                    } else {
                        Button(updater.repository == nil ? "Connect…":"Manage…") { NotificationCenter.default.post(name:.showUpdateConnection,object:nil) }
                            .buttonStyle(NashiButtonStyle(.secondary,compact:true))
                    }
                }.padding(18).nashiCard()
                if let error=updater.configurationError {
                    GroupBox("Updates are being set up") {
                        LText(error).font(.system(size:13)).foregroundStyle(.secondary).frame(maxWidth:.infinity,alignment:.leading).padding(10)
                    }
                } else {
                    GroupBox {
                        VStack(alignment:.leading,spacing:17) {
                            Toggle("Check for updates automatically",isOn:Binding(get:{updater.automaticallyChecksForUpdates},set:updater.setAutomaticChecks))
                            Text("Nashi checks once an hour while it’s running. You choose when to download and install.").font(.caption).foregroundStyle(.secondary)
                            Divider()
                            HStack {
                                VStack(alignment:.leading,spacing:5) {
                                    LText(statusTitle).font(.headline)
                                    if let date=updater.lastCheckedAt { Text("Last checked \(date,format:Date.FormatStyle(date:.abbreviated,time:.shortened))").font(.caption).foregroundStyle(.secondary) }
                                    else { Text("No update checks yet").font(.caption).foregroundStyle(.secondary) }
                                }
                                Spacer()
                                Button(updater.availableVersion == nil ? "Check for Updates…":"Update…") { updater.checkForUpdates() }
                                    .buttonStyle(NashiButtonStyle(.primary))
                                    .disabled(!updater.canCheckForUpdates || updater.isBusy || updater.waitingForCurrentTask)
                            }
                            if updater.isBusy { Label("Your current task will finish before an update restarts the app.",systemImage:"hourglass").font(.caption).foregroundStyle(.secondary) }
                            if let error=updater.lastError { Label(error,systemImage:"exclamationmark.circle").font(.caption).foregroundStyle(.secondary).textSelection(.enabled) }
                        }.padding(10)
                    }
                }
                Label("Updates are verified before installation. Your settings and recent history stay with you.",systemImage:"checkmark.shield")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
            }.padding(27).frame(maxWidth:680)
        }.frame(maxWidth:.infinity).nashiAnimation(value:updater.availableVersion)
    }

    private var statusTitle:String {
        if updater.waitingForCurrentTask { return "Ready after your current task" }
        if let version=updater.availableVersion { return "Version \(version) is available" }
        if updater.sessionInProgress { return "Checking for a fresh version…" }
        if updater.lastError != nil { return "The update needs another try" }
        return "Ready to check for updates"
    }
}

struct UpdateConnectionView:View {
    @ObservedObject var updater:AppUpdater
    @State private var repository=""
    @State private var token=""
    @State private var privateRepository=true
    @State private var message:String?
    @State private var error:String?

    var body:some View {
        ScrollView {
            VStack(alignment:.leading,spacing:20) {
                NashiPageHeading(eyebrow:"Update connection",title:"Fresh versions, one click away.",subtitle:"Connect your GitHub releases once. Nashi handles the next update checks.")
                Text("Nashi \(Bundle.main.object(forInfoDictionaryKey:"CFBundleShortVersionString") as? String ?? "Development") · Build \(Bundle.main.object(forInfoDictionaryKey:"CFBundleVersion") as? String ?? "—")")
                    .font(.caption).foregroundStyle(.secondary)
                if let connected=updater.repository {
                    Label("Connected to \(connected.slug)",systemImage:"checkmark.circle.fill")
                        .font(.system(size:13,weight:.semibold)).foregroundStyle(NashiPalette.forest)
                }
                VStack(alignment:.leading,spacing:14) {
                    Text("GitHub repository").font(.headline)
                    TextField("owner/repository or GitHub URL",text:$repository)
                        .textFieldStyle(.roundedBorder).accessibilityLabel("GitHub repository")
                    Toggle("This repository is private",isOn:$privateRepository)
                    if privateRepository {
                        Text("Fine-grained access token").font(.headline)
                        SecureField(updater.hasSavedToken && repositoryMatches ? "Saved token · Leave blank to keep it":"Paste your GitHub token",text:$token)
                            .textFieldStyle(.roundedBorder).accessibilityLabel("GitHub token").privacySensitive()
                        if updater.hasSavedToken && repositoryMatches {
                            Button("Authorize Saved Token…") { token="";save() }
                                .buttonStyle(NashiButtonStyle(.quiet,compact:true))
                            Text("Use once if macOS needs permission for your saved update connection.")
                                .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
                        }
                        Text("Select only this repository and give Contents read access. The token stays in this Mac’s Keychain.")
                            .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
                        Link("Create a fine-grained token on GitHub",destination:URL(string:"https://github.com/settings/personal-access-tokens/new")!)
                            .font(.caption)
                    } else {
                        Label("Public updates work without a token.",systemImage:"lock.open").font(.caption).foregroundStyle(.secondary)
                    }
                }.padding(20).nashiCard().disabled(!updater.canChangeConnection)
                if let message {
                    Label(message,systemImage:"checkmark.circle").font(.system(size:12)).foregroundStyle(NashiPalette.forest).fixedSize(horizontal:false,vertical:true)
                }
                if let error {
                    Label(error,systemImage:"exclamationmark.circle").font(.system(size:12)).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
                }
                if let configurationError=updater.configurationError {
                    Label(configurationError,systemImage:"wrench.and.screwdriver").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
                }
                HStack(spacing:12) {
                    if updater.isConnecting { ProgressView().controlSize(.small);Text("Checking the connection…").font(.caption) }
                    else if updater.repository != nil {
                        Button("Disconnect & Forget Token") { disconnect() }
                            .buttonStyle(NashiButtonStyle(.quiet,compact:true)).disabled(!updater.canChangeConnection)
                    }
                    Spacer()
                    Button("Save Connection") { save() }
                        .buttonStyle(NashiButtonStyle(.primary)).keyboardShortcut(.defaultAction)
                        .disabled(!updater.canChangeConnection || repository.trimmingCharacters(in:.whitespacesAndNewlines).isEmpty)
                }
                if updater.isConfigured {
                    Divider()
                    HStack {
                        VStack(alignment:.leading,spacing:5) {
                            Text(updater.isPreparingCheck ? "Checking releases…":"Ready for a fresh version?").font(.headline)
                            if let checked=updater.lastCheckedAt { Text("Last checked \(checked,format:Date.FormatStyle(date:.abbreviated,time:.shortened))").font(.caption).foregroundStyle(.secondary) }
                        }
                        Spacer()
                        Button(updater.availableVersion == nil ? "Check for Updates…":"Update…") { updater.checkForUpdates() }
                            .buttonStyle(NashiButtonStyle(.secondary,compact:true)).disabled(!updater.canCheckForUpdates || updater.isBusy)
                    }
                    Toggle("Check automatically while Nashi is running",isOn:Binding(get:{updater.automaticallyChecksForUpdates},set:updater.setAutomaticChecks))
                        .font(.system(size:12))
                    if let lastError=updater.lastError { Label(lastError,systemImage:"exclamationmark.circle").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true) }
                }
                Text("Updates are signed and verified before installation. Changing repositories keeps the same trusted signing key. Your settings and files stay with you.")
                    .font(.caption).foregroundStyle(.secondary).fixedSize(horizontal:false,vertical:true)
            }.padding(28)
        }.frame(minWidth:530,minHeight:570).background(NashiPalette.canvas)
        .onAppear {
            repository=updater.repository?.slug ?? updater.officialRepository?.slug ?? Bundle.main.object(forInfoDictionaryKey:"NashiSuggestedGitHubRepository") as? String ?? ""
            privateRepository=updater.requiresToken
        }
        .onDisappear { token="" }
    }

    private var repositoryMatches:Bool {
        (try? NashiUpdates.GitHubRepository(repository)) == updater.repository && updater.repository != nil
    }

    private func save() {
        message=nil;error=nil
        Task { @MainActor in
            do {
                try await updater.connectGitHub(repository:repository,token:token,requiresToken:privateRepository)
                token=""
                repository=updater.repository?.slug ?? repository
                message=updater.isConfigured ? "Connection saved. Future checks use it automatically.":"Connection saved. This build still needs its release signing configuration."
            } catch { self.error=error.localizedDescription }
        }
    }

    private func disconnect() {
        Task { @MainActor in
            do {
                try await updater.disconnectGitHub()
                token="";message="Connection removed and its saved token deleted.";error=nil
            } catch { self.error=error.localizedDescription;message=nil }
        }
    }
}
