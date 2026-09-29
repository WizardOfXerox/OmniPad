import SwiftUI

/// Main Gamepad Container View hosting the full-screen WKWebView and
/// an interactive gaming HUD overlay with server discovery and controls.
public struct ContentView: View {
    @StateObject private var appState = AppState.shared
    @StateObject private var udpTransport = UdpTransport.shared
    @StateObject private var serverDiscovery = ServerDiscovery.shared
    @StateObject private var volumeBumper = VolumeButtonBumper.shared

    @State private var showQrScanner = false
    @State private var showServerPicker = false
    @State private var showManualIpAlert = false
    @State private var manualIpInput = ""
    @State private var isHudExpanded = false

    public init() {}

    public var body: some View {
        ZStack {
            // 1. Dark Gamepad Background
            Color(red: 0.04, green: 0.05, blue: 0.08)
                .ignoresSafeArea()

            // 2. Full-Screen Edge-to-Edge Gamepad Web View
            WebViewContainer(
                appState: appState,
                onScanQrRequested: { showQrScanner = true },
                onServerSelectRequested: {
                    serverDiscovery.scan()
                    showServerPicker = true
                }
            )
            .ignoresSafeArea()

            // 3. Floating Quick Status Pill (Top Center)
            VStack {
                HStack(spacing: 8) {
                    Button(action: {
                        withAnimation(.spring(response: 0.35, dampingFraction: 0.8)) {
                            isHudExpanded.toggle()
                        }
                    }) {
                        HStack(spacing: 6) {
                            Circle()
                                .fill(connectionStatusColor)
                                .frame(width: 8, height: 8)

                            Text(connectionStatusText)
                                .font(.system(size: 11, weight: .bold, design: .monospaced))
                                .foregroundColor(.white)

                            if case .connected = udpTransport.status {
                                Text("\(udpTransport.streamRateHz) Hz")
                                    .font(.system(size: 10, weight: .medium, design: .monospaced))
                                    .foregroundColor(.cyan)

                                if udpTransport.latencyMs > 0 {
                                    Text("\(udpTransport.latencyMs)ms")
                                        .font(.system(size: 10, weight: .medium, design: .monospaced))
                                        .foregroundColor(.green)
                                }
                            }

                            Image(systemName: isHudExpanded ? "chevron.up" : "chevron.down")
                                .font(.system(size: 9, weight: .bold))
                                .foregroundColor(.gray)
                        }
                        .padding(.horizontal, 10)
                        .padding(.vertical, 5)
                        .background(Color(red: 0.08, green: 0.11, blue: 0.18).opacity(0.85))
                        .clipShape(Capsule())
                        .overlay(Capsule().stroke(Color.white.opacity(0.12), lineWidth: 1))
                        .shadow(color: .black.opacity(0.4), radius: 4, x: 0, y: 2)
                    }

                    Spacer()
                }
                .padding(.top, 8)
                .padding(.leading, 16)

                // 4. Expanded HUD Control Center
                if isHudExpanded {
                    hudControlCenter
                        .transition(.asymmetric(
                            insertion: .opacity.combined(with: .scale(scale: 0.95, anchor: .topLeading)),
                            removal: .opacity.combined(with: .scale(scale: 0.95, anchor: .topLeading))
                        ))
                }

                Spacer()
            }
        }
        .sheet(isPresented: $showQrScanner) {
            QrScannerView(
                onScanSuccess: { server in
                    showQrScanner = false
                    appState.connect(to: server)
                },
                onDismiss: { showQrScanner = false }
            )
            .ignoresSafeArea()
        }
        .sheet(isPresented: $showServerPicker) {
            ServerPickerView(
                servers: serverDiscovery.discoveredServers,
                isScanning: serverDiscovery.isScanning,
                onRescan: { serverDiscovery.scan() },
                onSelectServer: { server in
                    showServerPicker = false
                    appState.connect(to: server)
                },
                onScanQr: {
                    showServerPicker = false
                    showQrScanner = true
                },
                onManualIp: {
                    showServerPicker = false
                    showManualIpAlert = true
                },
                onOfflineMode: {
                    showServerPicker = false
                    appState.loadOffline()
                }
            )
        }
        .alert("Connect to PC Server", isPresented: $showManualIpAlert) {
            TextField("PC IP (e.g. 192.168.1.100)", text: $manualIpInput)
                .textInputAutocapitalization(.never)
                .disableAutocorrection(true)
            Button("Connect") {
                if !manualIpInput.isEmpty {
                    appState.connect(host: manualIpInput)
                }
            }
            Button("Cancel", role: .cancel) {}
        } message: {
            Text("Enter the local Wi-Fi IP address shown in your OmniPad Windows PC Server console.")
        }
        .onAppear {
            setupInitialConnection()
        }
    }

    private var hudControlCenter: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                Text("OmniPad Control Panel")
                    .font(.system(size: 14, weight: .bold))
                    .foregroundColor(.white)

                Spacer()

                Button(action: {
                    withAnimation { isHudExpanded = false }
                }) {
                    Image(systemName: "xmark.circle.fill")
                        .font(.system(size: 16))
                        .foregroundColor(.gray)
                }
            }

            Divider().background(Color.white.opacity(0.15))

            // Action grid
            LazyVGrid(columns: [GridItem(.flexible()), GridItem(.flexible())], spacing: 8) {
                HudButton(icon: "qrcode.viewfinder", title: "Scan QR", color: .cyan) {
                    isHudExpanded = false
                    showQrScanner = true
                }

                HudButton(icon: "network", title: "Wi-Fi Servers", color: .blue) {
                    isHudExpanded = false
                    serverDiscovery.scan()
                    showServerPicker = true
                }

                HudButton(icon: "keyboard", title: "Enter IP", color: .orange) {
                    isHudExpanded = false
                    manualIpInput = appState.currentServerIp
                    showManualIpAlert = true
                }

                HudButton(icon: "folder.fill", title: "Offline / Edit", color: .purple) {
                    isHudExpanded = false
                    appState.loadOffline()
                }

                HudButton(icon: "waveform.path", title: "Test Haptics", color: .pink) {
                    HapticEngine.shared.vibrateHeavy(ms: 80)
                }

                HudButton(
                    icon: appState.isVolumeBumperEnabled ? "speaker.slash.fill" : "speaker.wave.2.fill",
                    title: appState.isVolumeBumperEnabled ? "Bumpers: ON" : "Bumpers: OFF",
                    color: appState.isVolumeBumperEnabled ? .green : .gray
                ) {
                    appState.isVolumeBumperEnabled.toggle()
                }
            }

            // Info footer
            HStack {
                if appState.isOfflineMode {
                    Text("Offline Customizer Mode")
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(.gray)
                } else if !appState.currentServerIp.isEmpty {
                    Text("Server: \(appState.currentServerIp):\(appState.currentServerPort)")
                        .font(.system(size: 11, weight: .medium, design: .monospaced))
                        .foregroundColor(.gray)
                }

                Spacer()

                if appState.isVolumeBumperEnabled {
                    Text("Vol Up=LB  Vol Dn=RB")
                        .font(.system(size: 10, weight: .semibold))
                        .foregroundColor(.green.opacity(0.8))
                }
            }
        }
        .padding(14)
        .frame(maxWidth: 340)
        .background(Color(red: 0.08, green: 0.10, blue: 0.16).opacity(0.96))
        .cornerRadius(16)
        .overlay(RoundedRectangle(cornerRadius: 16).stroke(Color.white.opacity(0.12), lineWidth: 1))
        .shadow(color: .black.opacity(0.6), radius: 12, x: 0, y: 6)
        .padding(.leading, 16)
    }

    private var connectionStatusColor: Color {
        if appState.isOfflineMode { return .purple }
        switch udpTransport.status {
        case .connected: return .green
        case .connecting: return .yellow
        case .failed: return .red
        case .disconnected: return .gray
        }
    }

    private var connectionStatusText: String {
        if appState.isOfflineMode { return "OFFLINE" }
        switch udpTransport.status {
        case .connected(_, let slot):
            return slot == OmniPadProtocol.noPad ? "CONNECTED" : "P\(slot + 1)"
        case .connecting:
            return "CONNECTING..."
        case .failed:
            return "FAILED"
        case .disconnected:
            return appState.currentServerIp.isEmpty ? "DISCONNECTED" : appState.currentServerIp
        }
    }

    private func setupInitialConnection() {
        if !appState.currentServerIp.isEmpty {
            // Reconnect to saved IP
            appState.connect(host: appState.currentServerIp, port: appState.currentServerPort)
        } else {
            // Trigger auto-discovery
            serverDiscovery.scan(timeout: 1.2) { servers in
                if servers.count == 1 {
                    appState.connect(to: servers[0])
                } else if servers.isEmpty {
                    appState.loadOffline()
                }
            }
        }
    }
}

private struct HudButton: View {
    let icon: String
    let title: String
    let color: Color
    let action: () -> Void

    var body: some View {
        Button(action: {
            HapticEngine.shared.triggerTactileClick()
            action()
        }) {
            HStack(spacing: 6) {
                Image(systemName: icon)
                    .font(.system(size: 12, weight: .semibold))
                    .foregroundColor(color)
                Text(title)
                    .font(.system(size: 11, weight: .semibold))
                    .foregroundColor(.white)
                Spacer()
            }
            .padding(.horizontal, 10)
            .padding(.vertical, 8)
            .background(Color.white.opacity(0.06))
            .cornerRadius(8)
        }
    }
}

/// Server selection sheet view with discovery list
private struct ServerPickerView: View {
    let servers: [DiscoveredServer]
    let isScanning: Bool
    let onRescan: () -> Void
    let onSelectServer: (DiscoveredServer) -> Void
    let onScanQr: () -> Void
    let onManualIp: () -> Void
    let onOfflineMode: () -> Void

    @Environment(\.dismiss) private var dismiss

    var body: some View {
        NavigationView {
            ZStack {
                Color(red: 0.05, green: 0.07, blue: 0.11).ignoresSafeArea()

                VStack(spacing: 16) {
                    // Top Actions
                    HStack(spacing: 10) {
                        Button(action: onScanQr) {
                            Label("Scan QR", systemImage: "qrcode.viewfinder")
                                .font(.system(size: 13, weight: .bold))
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 10)
                                .background(Color.blue)
                                .foregroundColor(.white)
                                .cornerRadius(10)
                        }

                        Button(action: onManualIp) {
                            Label("Manual IP", systemImage: "plus")
                                .font(.system(size: 13, weight: .bold))
                                .frame(maxWidth: .infinity)
                                .padding(.vertical, 10)
                                .background(Color.white.opacity(0.1))
                                .foregroundColor(.white)
                                .cornerRadius(10)
                        }
                    }
                    .padding(.horizontal)

                    // Servers list
                    if servers.isEmpty {
                        VStack(spacing: 12) {
                            Spacer()
                            if isScanning {
                                ProgressView()
                                    .progressViewStyle(CircularProgressViewStyle(tint: .cyan))
                                    .scaleEffect(1.2)
                                Text("Searching local Wi-Fi for PC Servers...")
                                    .font(.system(size: 13, weight: .medium))
                                    .foregroundColor(.gray)
                            } else {
                                Image(systemName: "desktopcomputer.trianglebadge.exclamationmark")
                                    .font(.system(size: 40))
                                    .foregroundColor(.gray)
                                Text("No OmniPad PC Servers Found")
                                    .font(.system(size: 15, weight: .semibold))
                                    .foregroundColor(.white)
                                Text("Make sure OmniPad Server is running on your PC and both devices are on the same Wi-Fi.")
                                    .font(.system(size: 12))
                                    .foregroundColor(.gray)
                                    .multilineTextAlignment(.center)
                                    .padding(.horizontal, 32)
                            }
                            Spacer()
                        }
                    } else {
                        List {
                            Section(header: Text("DISCOVERED PC SERVERS").font(.caption).foregroundColor(.cyan)) {
                                ForEach(servers) { server in
                                    Button(action: { onSelectServer(server) }) {
                                        HStack {
                                            Image(systemName: "desktopcomputer")
                                                .font(.system(size: 20))
                                                .foregroundColor(.cyan)

                                            VStack(alignment: .leading, spacing: 2) {
                                                Text(server.name.isEmpty ? "Windows PC" : server.name)
                                                    .font(.system(size: 14, weight: .bold))
                                                    .foregroundColor(.white)
                                                Text("\(server.ip):\(server.port)")
                                                    .font(.system(size: 11, design: .monospaced))
                                                    .foregroundColor(.gray)
                                            }

                                            Spacer()

                                            Image(systemName: "chevron.right")
                                                .font(.system(size: 12))
                                                .foregroundColor(.gray)
                                        }
                                        .padding(.vertical, 4)
                                    }
                                    .listRowBackground(Color(red: 0.08, green: 0.11, blue: 0.18))
                                }
                            }
                        }
                        .listStyle(InsetGroupedListStyle())
                    }

                    // Bottom offline button
                    Button(action: onOfflineMode) {
                        Text("Use Offline / Layout Customizer")
                            .font(.system(size: 13, weight: .semibold))
                            .foregroundColor(.purple)
                            .padding(.bottom, 8)
                    }
                }
                .padding(.top)
            }
            .navigationTitle("Select Server")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Cancel") { dismiss() }
                        .foregroundColor(.gray)
                }
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button(action: onRescan) {
                        if isScanning {
                            ProgressView()
                                .progressViewStyle(CircularProgressViewStyle(tint: .white))
                        } else {
                            Image(systemName: "arrow.clockwise")
                                .foregroundColor(.cyan)
                        }
                    }
                }
            }
        }
    }
}
