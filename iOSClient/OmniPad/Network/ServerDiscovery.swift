import Foundation
import Network

/// Discovers OmniPad PC servers on the local network via UDP broadcast on port 27501.
public final class ServerDiscovery: ObservableObject {
    public static let shared = ServerDiscovery()

    @Published public var discoveredServers: [DiscoveredServer] = []
    @Published public var isScanning = false

    private let discoveryQueue = DispatchQueue(label: "com.omnipad.discovery", qos: .utility)

    public init() {}

    /// Broadcasts discovery probe on port 27501 and collects responding servers.
    public func scan(timeout: TimeInterval = 1.5, completion: (([DiscoveredServer]) -> Void)? = nil) {
        discoveryQueue.async { [weak self] in
            guard let self = self else { return }

            DispatchQueue.main.async {
                self.isScanning = true
                self.discoveredServers.removeAll()
            }

            var found = [DiscoveredServer]()
            var seenKeys = Set<String>()

            // Create UDP broadcast socket using POSIX
            let sock = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
            if sock >= 0 {
                var broadcastEnable: Int32 = 1
                setsockopt(sock, SOL_SOCKET, SO_BROADCAST, &broadcastEnable, socklen_t(MemoryLayout<Int32>.size))

                var tv = timeval(tv_sec: 0, tv_usec: 200_000) // 200ms per read
                setsockopt(sock, SOL_SOCKET, SO_RCVTIMEO, &tv, socklen_t(MemoryLayout<timeval>.size))

                // Send discovery probe [0xDA, 0x01, 0x06, 0xFF]
                var probe: [UInt8] = [
                    OmniPadProtocol.magicByte,
                    OmniPadProtocol.version,
                    OmniPadProtocol.msgDiscover,
                    OmniPadProtocol.noPad
                ]

                var destAddr = sockaddr_in()
                destAddr.sin_family = sa_family_t(AF_INET)
                destAddr.sin_port = in_port_t(OmniPadProtocol.discoveryPort).bigEndian
                inet_pton(AF_INET, "255.255.255.255", &destAddr.sin_addr)

                withUnsafePointer(to: &destAddr) { ptr in
                    ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { saPtr in
                        _ = sendto(sock, &probe, probe.count, 0, saPtr, socklen_t(MemoryLayout<sockaddr_in>.size))
                    }
                }

                // Read replies until timeout
                var buffer = [UInt8](repeating: 0, count: 256)
                let startTime = Date().timeIntervalSince1970

                while Date().timeIntervalSince1970 - startTime < timeout {
                    var senderAddr = sockaddr_in()
                    var senderLen = socklen_t(MemoryLayout<sockaddr_in>.size)

                    let received = withUnsafeMutablePointer(to: &senderAddr) { ptr in
                        ptr.withMemoryRebound(to: sockaddr.self, capacity: 1) { saPtr in
                            recvfrom(sock, &buffer, buffer.count, 0, saPtr, &senderLen)
                        }
                    }

                    if received >= 4 &&
                        buffer[0] == OmniPadProtocol.magicByte &&
                        buffer[1] == OmniPadProtocol.version &&
                        buffer[2] == OmniPadProtocol.msgWelcome {

                        var ipBuffer = [CChar](repeating: 0, count: Int(INET_ADDRSTRLEN))
                        inet_ntop(AF_INET, &senderAddr.sin_addr, &ipBuffer, socklen_t(INET_ADDRSTRLEN))
                        let serverIp = String(cString: ipBuffer)

                        var webPort = OmniPadProtocol.defaultWebPort
                        var machineName = ""

                        if received >= 7 {
                            let portLow = Int(buffer[4])
                            let portHigh = Int(buffer[5])
                            let parsedPort = (portHigh << 8) | portLow
                            if parsedPort > 0 && parsedPort <= 65535 {
                                webPort = parsedPort
                            }
                            let nameLen = Int(buffer[6])
                            if received >= 7 + nameLen && nameLen > 0 {
                                let nameBytes = Array(buffer[7..<(7 + nameLen)])
                                machineName = String(bytes: nameBytes, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines) ?? ""
                            }
                        }

                        let key = "\(serverIp):\(webPort)"
                        if !seenKeys.contains(key) {
                            seenKeys.insert(key)
                            let server = DiscoveredServer(ip: serverIp, name: machineName, port: webPort)
                            found.append(server)

                            DispatchQueue.main.async {
                                self.discoveredServers = found
                            }
                        }
                    }
                }

                close(sock)
            }

            DispatchQueue.main.async {
                self.isScanning = false
                self.discoveredServers = found
                completion?(found)
            }
        }
    }
}
