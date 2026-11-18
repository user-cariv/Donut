import SwiftUI
import CryptoKit
import CommonCrypto
import AppKit
import LocalAuthentication
import Security
import UniformTypeIdentifiers
import Combine

@main
struct DonutLockApp: App {
    @StateObject private var vault = VaultStore()
    var body: some Scene {
        WindowGroup { ContentView().environmentObject(vault).frame(minWidth: 720, minHeight: 520) }
    }
}

struct ContentView: View {
    @EnvironmentObject var vault: VaultStore
    @State private var password = ""
    @State private var selected: VaultEntry?
    @State private var showPicker = false
    var body: some View {
        VStack(spacing: 0) {
            HStack(spacing: 14) {
                Image("donut-lock-logo", bundle: .module).resizable().scaledToFill().frame(width: 58, height: 58).clipShape(RoundedRectangle(cornerRadius: 14))
                VStack(alignment: .leading, spacing: 3) { Text("Donut Lock").font(.title2.bold()); Text("Your private file vault").foregroundStyle(.secondary) }
                Spacer()
                Button { showPicker = true } label: { Label("Add files or folders", systemImage: "plus").fontWeight(.semibold).padding(.horizontal, 16).padding(.vertical, 10) }.buttonStyle(.borderedProminent)
            }.padding(24)
            Divider()
            HStack {
                Label("VAULT ITEMS", systemImage: "square.stack.3d.up.fill").font(.caption.bold()).tracking(1.1).foregroundStyle(.secondary)
                Spacer(); Text("\(vault.entries.count) items").font(.caption).foregroundStyle(.secondary)
            }.padding(.horizontal, 26).padding(.top, 23).padding(.bottom, 10)
            if vault.entries.isEmpty {
                Spacer()
                Image("donut-lock-logo", bundle: .module).resizable().scaledToFit().frame(width: 132, height: 132).padding(.bottom, 12)
                Text("Your vault is ready").font(.title3.bold())
                Text("Add a file or folder to create an encrypted vault copy.").foregroundStyle(.secondary).padding(.top, 4)
                Button { showPicker = true } label: { Label("Choose a file", systemImage: "plus").padding(.horizontal, 17).padding(.vertical, 10) }.buttonStyle(.borderedProminent).padding(.top, 17)
                Spacer()
            } else {
                List(vault.entries) { entry in
                    HStack(spacing: 14) {
                        Image(systemName: "doc.zipper").font(.title3).foregroundStyle(.indigo).frame(width: 42, height: 42).background(.indigo.opacity(0.08), in: RoundedRectangle(cornerRadius: 12))
                        VStack(alignment: .leading, spacing: 4) { Text(entry.name).fontWeight(.medium); Text("Encrypted · Added \(entry.added, style: .date)").font(.caption).foregroundStyle(.secondary) }
                        Spacer()
                        Button { selected = entry } label: { Label("Unlock", systemImage: "lock.open").labelStyle(.titleAndIcon) }.buttonStyle(.bordered)
                        Button(role: .destructive) { vault.remove(entry) } label: { Image(systemName: "trash") }.buttonStyle(.borderless).help("Delete encrypted vault copy")
                    }.padding(.vertical, 5)
                }.listStyle(.plain)
            }
            HStack { Image(systemName: "info.circle"); Text(vault.message).lineLimit(2); Spacer() }.font(.caption).foregroundStyle(.secondary).padding(15).frame(maxWidth: .infinity, alignment: .leading).background(.quaternary.opacity(0.5))
        }
        .background(Color(nsColor: .windowBackgroundColor))
        .fileImporter(isPresented: $showPicker, allowedContentTypes: [.item, .folder], allowsMultipleSelection: true) { result in
            if case .success(let urls) = result, let url = urls.first {
                let alert = NSAlert(); alert.messageText = "Encrypt vault copies"; alert.informativeText = "Choose a password of at least 4 characters. The original items stay in place."; alert.addButton(withTitle: "Encrypt"); alert.addButton(withTitle: "Cancel")
                let stack = NSStackView(); stack.orientation = .vertical; stack.alignment = .leading; stack.spacing = 9
                let field = NSSecureTextField(frame: NSRect(x: 0, y: 0, width: 270, height: 24)); field.placeholderString = "Vault password (4+ characters)"
                let touch = NSButton(checkboxWithTitle: "Use Touch ID when available", target: nil, action: nil)
                let context = LAContext(); var authError: NSError?; let canTouchID = context.canEvaluatePolicy(.deviceOwnerAuthenticationWithBiometrics, error: &authError) && context.biometryType == .touchID
                touch.isEnabled = canTouchID
                stack.addArrangedSubview(field); stack.addArrangedSubview(touch); alert.accessoryView = stack
                if !canTouchID { alert.informativeText += "\nTouch ID is not enrolled or available. Set it up in System Settings > Touch ID & Password, or continue with your password." }
                let response = alert.runModal()
                if response == .alertFirstButtonReturn, field.stringValue.count >= 4 {
                    for itemURL in urls {
                        let access = itemURL.startAccessingSecurityScopedResource(); defer { if access { itemURL.stopAccessingSecurityScopedResource() } }
                        vault.add(itemURL, password: field.stringValue, useBiometrics: touch.state == .on && canTouchID)
                    }
                } else if response == .alertFirstButtonReturn { vault.message = "Use a password with at least 4 characters." }
            }
        }
        .sheet(item: $selected) { entry in PasswordPrompt(name: entry.name, hasBiometric: entry.hasBiometric) { vault.open(entry, password: $0); selected = nil } onCancel: { selected = nil }.frame(width: 380, height: 240) }
    }
}

struct PasswordPrompt: View {
    let name: String; let hasBiometric: Bool; let onUnlock: (String) -> Void; let onCancel: () -> Void
    @State private var password = ""
    var body: some View { VStack(alignment: .leading, spacing: 14) { Text("Unlock item").font(.title2.bold()); Text(name).font(.subheadline).foregroundStyle(.secondary); SecureField("Vault password (4+ characters)", text: $password).textFieldStyle(.roundedBorder); Text(hasBiometric ? "Use Touch ID or enter your password." : "Touch ID wasn’t enabled for this item. You can unlock with your password.").font(.caption).foregroundStyle(.secondary); Spacer(); HStack { if hasBiometric { Button { onUnlock("__TOUCH_ID__") } label: { Label("Use Touch ID", systemImage: "touchid") } }; Spacer(); Button("Cancel", action: onCancel); Button("Unlock") { onUnlock(password) }.buttonStyle(.borderedProminent).disabled(password.count < 4) } }.padding(24) }
}

struct VaultEntry: Identifiable, Codable {
    var id: UUID = UUID()
    var name: String
    var file: String
    var added: Date = Date()
    var isFolder: Bool = false
    var hasBiometric: Bool = false
}

@MainActor final class VaultStore: ObservableObject {
    @Published var entries: [VaultEntry] = []
    @Published var message = "Your files stay encrypted in this Mac’s vault."
    private let base = FileManager.default.urls(for: .applicationSupportDirectory, in: .userDomainMask)[0].appendingPathComponent("DonutLock", isDirectory: true)
    private var index: URL { base.appendingPathComponent("index.json") }
    init() { try? FileManager.default.createDirectory(at: base, withIntermediateDirectories: true); entries = (try? JSONDecoder().decode([VaultEntry].self, from: Data(contentsOf: index))) ?? [] }
    func saveIndex() { try? JSONEncoder().encode(entries).write(to: index, options: .atomic) }
    func add(_ url: URL, password: String, useBiometrics: Bool) {
        do {
            var source = url; var zipped: URL?
            let isFolder = (try? url.resourceValues(forKeys: [.isDirectoryKey]).isDirectory) == true
            if isFolder { let archive = FileManager.default.temporaryDirectory.appendingPathComponent(UUID().uuidString + ".zip"); try Self.runDitto(["-c", "-k", "--sequesterRsrc", "--keepParent", url.path, archive.path]); source = archive; zipped = archive }
            defer { if let zipped { try? FileManager.default.removeItem(at: zipped) } }
            let bytes = try Data(contentsOf: source)
            let salt = Data((0..<16).map { _ in UInt8.random(in: 0...255) })
            let key = try Self.derive(password, salt: salt)
            let sealed = try AES.GCM.seal(bytes, using: key)
            guard let box = sealed.combined else { throw CocoaError(.fileWriteUnknown) }
            let name = UUID().uuidString + ".vault"
            try (salt + box).write(to: base.appendingPathComponent(name), options: .atomic)
            let entry = VaultEntry(name: url.lastPathComponent, file: name, isFolder: isFolder, hasBiometric: useBiometrics)
            if useBiometrics { try Self.storeTouchIDPassword(password, id: entry.id) }
            entries.insert(entry, at: 0); saveIndex()
            message = "Added an encrypted copy of \(url.lastPathComponent)."
        } catch { message = "Could not add file: \(error.localizedDescription)" }
    }
    func open(_ entry: VaultEntry, password: String) {
        do {
            var actualPassword = password
            if password == "__TOUCH_ID__" { actualPassword = try Self.readTouchIDPassword(id: entry.id) }
            let blob = try Data(contentsOf: base.appendingPathComponent(entry.file)); let salt = blob.prefix(16)
            let key = try Self.derive(actualPassword, salt: Data(salt))
            let box = try AES.GCM.SealedBox(combined: Data(blob.dropFirst(16)))
            let clear = try AES.GCM.open(box, using: key)
            let temp = FileManager.default.temporaryDirectory.appendingPathComponent(entry.name + (entry.isFolder ? ".zip" : ""))
            try clear.write(to: temp, options: .atomic)
            if entry.isFolder { let folder = FileManager.default.temporaryDirectory.appendingPathComponent("DonutLock-" + UUID().uuidString, isDirectory: true); try FileManager.default.createDirectory(at: folder, withIntermediateDirectories: true); try Self.runDitto(["-x", "-k", temp.path, folder.path]); try? FileManager.default.removeItem(at: temp); NSWorkspace.shared.open(folder) }
            else { NSWorkspace.shared.open(temp) }
            message = "Opened a temporary decrypted copy. Close the file and remove it when finished."
        } catch { message = "Unlock failed. Check the password or vault data." }
    }
    func remove(_ entry: VaultEntry) { try? FileManager.default.removeItem(at: base.appendingPathComponent(entry.file)); if entry.hasBiometric { Self.deleteTouchIDPassword(id: entry.id) }; entries.removeAll { $0.id == entry.id }; saveIndex() }
    private static let keychainService = "com.donut.lock.vault"
    private static func storeTouchIDPassword(_ password: String, id: UUID) throws {
        var error: Unmanaged<CFError>?
        guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenUnlockedThisDeviceOnly, .biometryCurrentSet, &error) else {
            if let error { throw error.takeRetainedValue() }
            throw NSError(domain: "DonutLock.Keychain", code: -1)
        }
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService, kSecAttrAccount as String: id.uuidString, kSecValueData as String: Data(password.utf8), kSecAttrAccessControl as String: access]
        SecItemDelete([kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService, kSecAttrAccount as String: id.uuidString] as CFDictionary)
        let status = SecItemAdd(query as CFDictionary, nil); guard status == errSecSuccess else { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }
    }
    private static func readTouchIDPassword(id: UUID) throws -> String {
        let query: [String: Any] = [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService, kSecAttrAccount as String: id.uuidString, kSecReturnData as String: true, kSecUseOperationPrompt as String: "Unlock with Touch ID"]
        var result: CFTypeRef?; let status = SecItemCopyMatching(query as CFDictionary, &result); guard status == errSecSuccess, let data = result as? Data, let value = String(data: data, encoding: .utf8) else { throw NSError(domain: NSOSStatusErrorDomain, code: Int(status)) }; return value
    }
    private static func deleteTouchIDPassword(id: UUID) { SecItemDelete([kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: keychainService, kSecAttrAccount as String: id.uuidString] as CFDictionary) }
    private static func runDitto(_ arguments: [String]) throws {
        let process = Process(); process.executableURL = URL(fileURLWithPath: "/usr/bin/ditto"); process.arguments = arguments
        let errors = Pipe(); process.standardError = errors; try process.run(); process.waitUntilExit()
        guard process.terminationStatus == 0 else { let data = errors.fileHandleForReading.readDataToEndOfFile(); throw NSError(domain: "DonutLock.Archive", code: Int(process.terminationStatus), userInfo: [NSLocalizedDescriptionKey: String(data: data, encoding: .utf8) ?? "Could not archive folder."]) }
    }
    private static func derive(_ password: String, salt: Data) throws -> SymmetricKey {
        let input = Array(password.utf8), s = Array(salt); var out = [UInt8](repeating: 0, count: 32)
        let status = input.withUnsafeBytes { inputBytes in s.withUnsafeBytes { saltBytes in out.withUnsafeMutableBytes { outputBytes in
            CCKeyDerivationPBKDF(CCPBKDFAlgorithm(kCCPBKDF2), inputBytes.baseAddress!.assumingMemoryBound(to: Int8.self), input.count, saltBytes.baseAddress!.assumingMemoryBound(to: UInt8.self), s.count, CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA256), 180_000, outputBytes.baseAddress!.assumingMemoryBound(to: UInt8.self), 32)
        } } }
        guard status == kCCSuccess else { throw CocoaError(.fileReadUnknown) }
        return SymmetricKey(data: out)
    }
}
