import Foundation
import SwiftUI
import WebKit

/// Central application state manager for OmniPad iOS.
public final class AppState: ObservableObject {
    public static let shared = AppState()

    private let defaults = UserDefaults.standard
    private let keyLastIp = "omnipad_last_server_ip"
    private let keyLastPort = "omnipad_last_server_port"
    private let keyBumperEnabled = "omnipad_bumper_enabled"

    @Published public var currentServerIp: String {
        didSet { defaults.set(currentServerIp, forKey: keyLastIp) }
    }

    @Published public var currentServerPort: Int {
        didSet { defaults.set(currentServerPort, forKey: keyLastPort) }
    }

    @Published public var isOfflineMode: Bool = false
    @Published public var reloadRequested: Bool = false
    @Published public var isWebLoaded: Bool = false
    @Published public var isHudVisible: Bool = false

    @Published public var isVolumeBumperEnabled: Bool {
        didSet {
            defaults.set(isVolumeBumperEnabled, forKey: keyBumperEnabled)
            VolumeButtonBumper.shared.isEnabled = isVolumeBumperEnabled
        }
    }

    public weak var activeWebView: WKWebView?

    public init() {
        let savedIp = defaults.string(forKey: keyLastIp) ?? ""
        let savedPort = defaults.integer(forKey: keyLastPort)
        let savedBumper = defaults.object(forKey: keyBumperEnabled) as? Bool ?? true

        self.currentServerIp = savedIp
        self.currentServerPort = savedPort > 0 ? savedPort : OmniPadProtocol.defaultWebPort
        self.isVolumeBumperEnabled = savedBumper
        VolumeButtonBumper.shared.isEnabled = savedBumper
    }

    public var serverUrl: URL? {
        guard !currentServerIp.trimmingCharacters(in: .whitespaces).isEmpty else { return nil }
        return URL(string: "http://\(currentServerIp):\(currentServerPort)/")
    }

    /// Connects to a specific server, starting both the WebClient and native UDP transport.
    public func connect(to server: DiscoveredServer) {
        self.currentServerIp = server.ip
        self.currentServerPort = server.port
        self.isOfflineMode = false
        self.reloadRequested = true

        // Connect native 250 Hz UDP streaming transport to port 27500
        UdpTransport.shared.connect(host: server.ip, port: OmniPadProtocol.defaultInputPort)
    }

    /// Connects to a raw host IP string.
    public func connect(host: String, port: Int = OmniPadProtocol.defaultWebPort) {
        self.currentServerIp = host.trimmingCharacters(in: .whitespaces)
        self.currentServerPort = port
        self.isOfflineMode = false
        self.reloadRequested = true

        UdpTransport.shared.connect(host: currentServerIp, port: OmniPadProtocol.defaultInputPort)
    }

    /// Loads the local bundled WebClient in offline mode.
    public func loadOffline() {
        self.isOfflineMode = true
        self.reloadRequested = true
        UdpTransport.shared.disconnect()
    }
}
