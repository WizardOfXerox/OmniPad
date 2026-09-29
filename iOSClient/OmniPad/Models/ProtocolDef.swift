import Foundation

/// Canonical OmniPad wire protocol definitions matching PCServer.Core.Protocol and Android client.
public enum OmniPadProtocol {
    public static let magicByte: UInt8 = 0xDA
    public static let version: UInt8 = 1

    public static let defaultInputPort: UInt16 = 27500
    public static let discoveryPort: UInt16 = 27501
    public static let defaultWebPort: Int = 27502
    public static let defaultDsuPort: UInt16 = 26760

    public static let inputPacketSize = 20
    public static let sessionMessageSize = 4
    public static let rumbleMessageSize = 6
    public static let motionPacketSize = 36
    public static let touchpadPacketSize = 13

    public static let noPad: UInt8 = 0xFF

    // Message Types
    public static let msgInput: UInt8 = 0x01
    public static let msgHello: UInt8 = 0x02
    public static let msgWelcome: UInt8 = 0x03
    public static let msgBye: UInt8 = 0x04
    public static let msgRumble: UInt8 = 0x05
    public static let msgDiscover: UInt8 = 0x06
    public static let msgPing: UInt8 = 0x07
    public static let msgPong: UInt8 = 0x08
    public static let msgSlotStatus: UInt8 = 0x09
    public static let msgSwitchSlot: UInt8 = 0x0A
    public static let msgMotion: UInt8 = 0x10
    public static let msgTouchpad: UInt8 = 0x11
    public static let msgSetControllerType: UInt8 = 0x12
    public static let msgActiveProfile: UInt8 = 0x13

    // Button Bitmasks (matches XINPUT_GAMEPAD + PS4 Touchpad)
    public struct Buttons: OptionSet {
        public let rawValue: UInt16

        public init(rawValue: UInt16) {
            self.rawValue = rawValue
        }

        public static let dpadUp        = Buttons(rawValue: 0x0001)
        public static let dpadDown      = Buttons(rawValue: 0x0002)
        public static let dpadLeft      = Buttons(rawValue: 0x0004)
        public static let dpadRight     = Buttons(rawValue: 0x0008)
        public static let start         = Buttons(rawValue: 0x0010)
        public static let back          = Buttons(rawValue: 0x0020)
        public static let leftThumb     = Buttons(rawValue: 0x0040)
        public static let rightThumb    = Buttons(rawValue: 0x0080)
        public static let leftShoulder  = Buttons(rawValue: 0x0100) // LB
        public static let rightShoulder = Buttons(rawValue: 0x0200) // RB
        public static let guide         = Buttons(rawValue: 0x0400)
        public static let touchpad      = Buttons(rawValue: 0x0800)
        public static let a             = Buttons(rawValue: 0x1000)
        public static let b             = Buttons(rawValue: 0x2000)
        public static let x             = Buttons(rawValue: 0x4000)
        public static let y             = Buttons(rawValue: 0x8000)
    }
}

/// Representation of physical/virtual gamepad state.
public struct GamepadState: Equatable {
    public var buttons: UInt16 = 0
    public var leftTrigger: UInt8 = 0
    public var rightTrigger: UInt8 = 0
    public var thumbLX: Int16 = 0
    public var thumbLY: Int16 = 0
    public var thumbRX: Int16 = 0
    public var thumbRY: Int16 = 0

    public init(
        buttons: UInt16 = 0,
        leftTrigger: UInt8 = 0,
        rightTrigger: UInt8 = 0,
        thumbLX: Int16 = 0,
        thumbLY: Int16 = 0,
        thumbRX: Int16 = 0,
        thumbRY: Int16 = 0
    ) {
        self.buttons = buttons
        self.leftTrigger = leftTrigger
        self.rightTrigger = rightTrigger
        self.thumbLX = thumbLX
        self.thumbLY = thumbLY
        self.thumbRX = thumbRX
        self.thumbRY = thumbRY
    }

    /// Serializes the state into the canonical 20-byte UDP binary packet.
    public func serialize(padSlot: UInt8, sequence: UInt32) -> Data {
        var data = Data(count: OmniPadProtocol.inputPacketSize)
        data[0] = OmniPadProtocol.magicByte
        data[1] = OmniPadProtocol.version
        data[2] = OmniPadProtocol.msgInput
        data[3] = padSlot

        // Sequence (u32 little-endian)
        var seqLE = sequence.littleEndian
        withUnsafeBytes(of: &seqLE) { data.replaceSubrange(4..<8, with: $0) }

        // Buttons (u16 little-endian)
        var btnsLE = buttons.littleEndian
        withUnsafeBytes(of: &btnsLE) { data.replaceSubrange(8..<10, with: $0) }

        // Triggers (u8, u8)
        data[10] = leftTrigger
        data[11] = rightTrigger

        // Axes (i16 little-endian)
        var lxLE = thumbLX.littleEndian
        withUnsafeBytes(of: &lxLE) { data.replaceSubrange(12..<14, with: $0) }

        var lyLE = thumbLY.littleEndian
        withUnsafeBytes(of: &lyLE) { data.replaceSubrange(14..<16, with: $0) }

        var rxLE = thumbRX.littleEndian
        withUnsafeBytes(of: &rxLE) { data.replaceSubrange(16..<18, with: $0) }

        var ryLE = thumbRY.littleEndian
        withUnsafeBytes(of: &ryLE) { data.replaceSubrange(18..<20, with: $0) }

        return data
    }
}

/// Metadata for a discovered OmniPad Windows PC Server.
public struct DiscoveredServer: Identifiable, Hashable, Equatable {
    public let id = UUID()
    public let ip: String
    public let name: String
    public let port: Int

    public init(ip: String, name: String, port: Int = OmniPadProtocol.defaultWebPort) {
        self.ip = ip
        self.name = name
        self.port = port
    }

    public var displayName: String {
        if !name.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty {
            return "\(name) (\(ip))"
        }
        return ip
    }

    public var webUrl: URL? {
        URL(string: "http://\(ip):\(port)/")
    }

    public func hash(into hasher: inout Hasher) {
        hasher.combine(ip)
        hasher.combine(port)
    }

    public static func == (lhs: DiscoveredServer, rhs: DiscoveredServer) -> Bool {
        lhs.ip == rhs.ip && lhs.port == rhs.port
    }
}
