import Foundation
import Network
import Combine

public enum TransportStatus: Equatable {
    case disconnected
    case connecting(String)
    case connected(String, UInt8) // host, assignedSlot
    case failed(String)
}

/// Ultra-low latency UDP streaming transport using Apple's Network.framework (NWConnection).
/// Streams 20-byte gamepad packets at up to 250 Hz (4ms tick) with sub-millisecond network jitter,
/// and processes bi-directional server rumble packets for CoreHaptics.
public final class UdpTransport: ObservableObject {
    public static let shared = UdpTransport()

    @Published public var status: TransportStatus = .disconnected
    @Published public var latencyMs: Int = 0
    @Published public var streamRateHz: Int = 0
    @Published public var assignedPadSlot: UInt8 = OmniPadProtocol.noPad
    @Published public var currentHost: String = ""
    @Published public var currentPort: UInt16 = OmniPadProtocol.defaultInputPort

    // Streaming state
    private var connection: NWConnection?
    private var streamTimer: DispatchSourceTimer?
    private var isStreaming = false
    private var sequence: UInt32 = 0
    private var currentState = GamepadState()

    // Metrics
    private var packetCounter: Int = 0
    private var metricsTimer: DispatchSourceTimer?
    private var lastPingSentTime: DispatchTime?

    private let transportQueue = DispatchQueue(label: "com.omnipad.transport.udp", qos: .userInteractive)

    public init() {}

    /// Connects to the OmniPad PC Server at the given IP and port.
    public func connect(host: String, port: UInt16 = OmniPadProtocol.defaultInputPort) {
        transportQueue.async { [weak self] in
            guard let self = self else { return }

            self.disconnectInternal()

            let cleanHost = host.trimmingCharacters(in: .whitespacesAndNewlines)
            guard !cleanHost.isEmpty else { return }

            DispatchQueue.main.async {
                self.currentHost = cleanHost
                self.currentPort = port
                self.status = .connecting(cleanHost)
            }

            let endpointHost = NWEndpoint.Host(cleanHost)
            guard let endpointPort = NWEndpoint.Port(rawValue: port) else {
                DispatchQueue.main.async { self.status = .failed("Invalid port: \(port)") }
                return
            }

            // Low-latency UDP parameters
            let params = NWParameters.udp
            params.serviceClass = .responsiveData
            params.allowFastOpen = true

            let conn = NWConnection(host: endpointHost, port: endpointPort, using: params)
            self.connection = conn

            conn.stateUpdateHandler = { [weak self] newState in
                self?.handleStateUpdate(newState)
            }

            conn.start(queue: self.transportQueue)

            // Send Hello handshake
            self.sendHello()

            // Begin receive loop for Rumble & Welcome packets
            self.receiveNextPacket()
        }
    }

    private func handleStateUpdate(_ state: NWConnection.State) {
        switch state {
        case .ready:
            print("[UdpTransport] Socket ready to \(currentHost):\(currentPort)")
            sendHello()
            startStreamTimer(rateHz: 250) // Default 250 Hz streaming loop
            startMetricsTimer()
        case .failed(let error):
            print("[UdpTransport] Connection error: \(error)")
            DispatchQueue.main.async {
                self.status = .failed(error.localizedDescription)
            }
        case .cancelled:
            DispatchQueue.main.async {
                self.status = .disconnected
                self.assignedPadSlot = OmniPadProtocol.noPad
            }
        default:
            break
        }
    }

    /// Continuously reads incoming UDP packets (Rumble, Welcome, Pong).
    private func receiveNextPacket() {
        connection?.receiveMessage { [weak self] content, context, isComplete, error in
            guard let self = self else { return }

            if let data = content, !data.isEmpty {
                self.processIncomingPacket(data)
            }

            if error == nil && self.connection != nil {
                self.receiveNextPacket()
            }
        }
    }

    /// Processes binary server messages matching the OmniPad protocol.
    private func processIncomingPacket(_ data: Data) {
        guard data.count >= 4 else { return }
        guard data[0] == OmniPadProtocol.magicByte, data[1] == OmniPadProtocol.version else { return }

        let msgType = data[2]

        switch msgType {
        case OmniPadProtocol.msgWelcome:
            // 4-byte WELCOME answer: [Magic, Ver, MsgWelcome, AssignedPadSlot]
            let slot = data[3]
            DispatchQueue.main.async {
                self.assignedPadSlot = slot
                self.status = .connected(self.currentHost, slot)
            }
            print("[UdpTransport] Server assigned Gamepad Slot: P\(slot == OmniPadProtocol.noPad ? 0 : slot + 1)")

        case OmniPadProtocol.msgRumble:
            // 6-byte RUMBLE: [Magic, Ver, MsgRumble, Slot, LargeMotor, SmallMotor]
            if data.count >= OmniPadProtocol.rumbleMessageSize {
                let large = data[4]
                let small = data[5]
                HapticEngine.shared.handleRumble(large: large, small: small)
            }

        case OmniPadProtocol.msgPong:
            // RTT latency measurement
            if let sent = self.lastPingSentTime {
                let nano = DispatchTime.now().uptimeNanoseconds - sent.uptimeNanoseconds
                let ms = max(1, Int(nano / 1_000_000))
                DispatchQueue.main.async {
                    self.latencyMs = ms
                }
            }

        default:
            break
        }
    }

    /// Sends the initial MsgHello handshake.
    private func sendHello() {
        var hello = Data(count: OmniPadProtocol.sessionMessageSize)
        hello[0] = OmniPadProtocol.magicByte
        hello[1] = OmniPadProtocol.version
        hello[2] = OmniPadProtocol.msgHello
        hello[3] = OmniPadProtocol.noPad

        connection?.send(content: hello, completion: .idempotent)
    }

    /// Updates the current cached gamepad state (called by touch engine or JS bridge).
    public func updateState(_ state: GamepadState) {
        transportQueue.async { [weak self] in
            self?.currentState = state
        }
    }

    /// Directly sends a 20-byte packet immediately (event-driven fast path).
    public func sendImmediate(buttons: UInt16, lx: Int16, ly: Int16, rx: Int16, ry: Int16, lt: UInt8, rt: UInt8) {
        transportQueue.async { [weak self] in
            guard let self = self else { return }
            self.currentState = GamepadState(
                buttons: buttons,
                leftTrigger: lt,
                rightTrigger: rt,
                thumbLX: lx,
                thumbLY: ly,
                thumbRX: rx,
                thumbRY: ry
            )
            self.transmitPacket()
        }
    }

    /// Starts high-frequency dispatch timer (up to 250 Hz = 4.0ms interval).
    public func startStreamTimer(rateHz: Int = 250) {
        streamTimer?.cancel()

        let timer = DispatchSource.makeTimerSource(queue: transportQueue)
        let intervalMs = max(1, 1000 / rateHz)
        timer.schedule(deadline: .now(), repeating: .milliseconds(intervalMs), leeway: .microseconds(500))

        timer.setEventHandler { [weak self] in
            self?.transmitPacket()
        }

        timer.resume()
        self.streamTimer = timer
        self.isStreaming = true
    }

    private func transmitPacket() {
        guard let conn = connection, conn.state == .ready else { return }

        sequence = sequence &+ 1
        let packetData = currentState.serialize(padSlot: assignedPadSlot, sequence: sequence)

        conn.send(content: packetData, completion: .idempotent)
        packetCounter += 1
    }

    private func startMetricsTimer() {
        metricsTimer?.cancel()
        let timer = DispatchSource.makeTimerSource(queue: transportQueue)
        timer.schedule(deadline: .now() + 1.0, repeating: .seconds(1))

        timer.setEventHandler { [weak self] in
            guard let self = self else { return }
            let count = self.packetCounter
            self.packetCounter = 0

            DispatchQueue.main.async {
                self.streamRateHz = count
            }

            // Send ping packet every second to measure roundtrip latency
            self.sendPing()
        }

        timer.resume()
        self.metricsTimer = timer
    }

    private func sendPing() {
        lastPingSentTime = DispatchTime.now()
        var ping = Data(count: 4)
        ping[0] = OmniPadProtocol.magicByte
        ping[1] = OmniPadProtocol.version
        ping[2] = OmniPadProtocol.msgPing
        ping[3] = assignedPadSlot
        connection?.send(content: ping, completion: .idempotent)
    }

    /// Disconnects from the server, sending MsgBye.
    public func disconnect() {
        transportQueue.async { [weak self] in
            self?.disconnectInternal()
        }
    }

    private func disconnectInternal() {
        streamTimer?.cancel()
        streamTimer = nil
        metricsTimer?.cancel()
        metricsTimer = nil
        isStreaming = false

        if let conn = connection, assignedPadSlot != OmniPadProtocol.noPad {
            var bye = Data(count: OmniPadProtocol.sessionMessageSize)
            bye[0] = OmniPadProtocol.magicByte
            bye[1] = OmniPadProtocol.version
            bye[2] = OmniPadProtocol.msgBye
            bye[3] = assignedPadSlot
            conn.send(content: bye, completion: .idempotent)
        }

        connection?.cancel()
        connection = nil

        DispatchQueue.main.async {
            self.status = .disconnected
            self.assignedPadSlot = OmniPadProtocol.noPad
            self.streamRateHz = 0
            self.latencyMs = 0
        }
    }

    deinit {
        disconnectInternal()
    }
}
