# OmniPad iOS Client

Native iOS client for **OmniPad** — turning your iPhone or iPad into an ultra-low latency, tactile virtual gaming controller for Windows PC gaming.

Built with **SwiftUI**, **WebKit**, Apple's **Network.framework** (`NWConnection`), **CoreHaptics** (`CHHapticEngine`), and **AVFoundation**.

---

## ✨ Features & Architecture Parity

- **🚀 Ultra-Low Latency UDP Streaming (up to 250 Hz)**: Uses Apple's modern `Network.framework` (`NWConnection`) with `.responsiveData` QoS to stream 20-byte XInput frames at sub-millisecond network jitter.
- **🎮 Physical Hardware Bumpers (LB & RB)**: Intercepts physical iOS Volume Up and Volume Down buttons via `AVAudioSession` and a hidden `MPVolumeView`, providing genuine physical trigger parity with the Android client without displaying the system volume HUD.
- **📳 Dual-Motor CoreHaptics Engine**: Translates PC Server dual-motor rumble packets (0–255) into high-fidelity tactile feedback using Apple's Taptic Engine (`CHHapticEngine`):
  - **Left Motor (Large)**: Low-frequency, heavy rumble (explosions, terrain impact, crashes) with low sharpness.
  - **Right Motor (Small)**: High-frequency crisp vibration (gun clicks, engine revs, buzz) with high sharpness.
- **📷 Fast QR Code Camera Scanner**: Integrated `AVCaptureSession` QR reader with a cyberpunk targeting reticle and torch control. Instantly pairs with the ASCII QR code displayed in the PC Server terminal or web UI.
- **📡 Automatic UDP Wi-Fi Discovery**: Broadcasts discovery probes on UDP port 27501 to find all active OmniPad PC servers on your Wi-Fi network without typing IP addresses.
- **📁 Offline Mode & Customizer**: Bundled with the full `WebClient` offline assets, allowing on-the-go button layout customization, test gamepad diagnostics, and standalone play.
- **🔄 Unified JavaScript Bridge (`window.OmniPadNative`)**: Seamless bidirectional bridge between the high-performance WebGL gamepad UI and native iOS subsystems.
- **⚡ ProMotion 120 Hz Display**: Unlocks high-refresh displays on iPhone 13 Pro, 14 Pro, 15 Pro, and 16 Pro models for razor-sharp input response.

---

## 📂 Project Structure

```
iOSClient/
├── OmniPad.xcodeproj/             # Turn-key Xcode project file
│   └── project.pbxproj
├── OmniPad/
│   ├── App/
│   │   └── OmniPadApp.swift       # SwiftUI App entry point, orientation & screen sleep locks
│   ├── Views/
│   │   ├── ContentView.swift      # Main view container with gaming HUD overlay & server sheets
│   │   └── WebViewContainer.swift # WKWebView UIViewRepresentable, touch optimizer, and JS bridge
│   ├── Network/
│   │   ├── UdpTransport.swift     # 250 Hz binary UDP streamer using NWConnection
│   │   └── ServerDiscovery.swift  # UDP broadcast scanner for port 27501 discovery
│   ├── Haptics/
│   │   └── HapticEngine.swift     # CoreHaptics dual-motor rumble & transient tap translator
│   ├── Hardware/
│   │   └── VolumeButtonBumper.swift # Volume keys -> LB / RB tactical hardware bumpers
│   ├── Scanner/
│   │   └── QrScannerView.swift    # AVCaptureSession camera QR scanner with targeting reticle
│   ├── Models/
│   │   ├── ProtocolDef.swift      # 20-byte frame definition, button bitmasks, and packet serializers
│   │   └── AppState.swift         # Central reactive state manager & connection persistence
│   ├── Resources/
│   │   └── Web/                   # Bundled offline WebClient (HTML, CSS, JS, audio assets)
│   └── Info.plist                 # Network & Camera privacy descriptions, landscape lock
└── README.md                      # Installation and build guide
```

---

## 🛠️ Build & Installation Guide

### Method A: Build with Xcode (Mac)

1. Open `iOSClient/OmniPad.xcodeproj` in **Xcode 14, 15, or 16+**.
2. Connect your iPhone or iPad via USB or Wi-Fi.
3. In Xcode, select the **OmniPad** target:
   - Go to **Signing & Capabilities**.
   - Under **Team**, select your Personal Apple ID or Developer Account.
   - If needed, change the **Bundle Identifier** to a unique ID (e.g., `com.yourname.omnipad`).
4. Select your device as the run destination and click **Run** (or press `Cmd + R`).
5. On your iOS device:
   - Go to **Settings > General > VPN & Device Management**.
   - Tap your Apple ID and choose **Trust "OmniPad"**.
   - Launch OmniPad and allow **Local Network** access when prompted.

---

### Method B: Sideloading on Windows (No Mac Required)

You can sideload OmniPad onto your iPhone or iPad directly from Windows using **AltStore**, **SideStore**, or **Sideloadly**.

#### 1. Packaging an `.ipa` File
If you have access to a Mac or a CI runner (such as GitHub Actions):
```bash
cd iOSClient
xcodebuild clean archive \
  -project OmniPad.xcodeproj \
  -scheme OmniPad \
  -archivePath build/OmniPad.xcarchive \
  -configuration Release \
  CODE_SIGNING_ALLOWED=NO

# Package into payload IPA
mkdir -p build/Payload
cp -R build/OmniPad.xcarchive/Products/Applications/OmniPad.app build/Payload/
cd build && zip -r OmniPad.ipa Payload
```

#### 2. Sideloading via Sideloadly (Recommended for Windows)
1. Download and install [Sideloadly](https://sideloadly.io/) on your Windows PC.
2. Install the non-Microsoft Store versions of **iTunes** and **iCloud** for Windows (required for device pairing).
3. Connect your iPhone to your PC via USB and ensure your iPhone is unlocked and says "Trust This Computer".
4. Open Sideloadly:
   - Drag and drop `OmniPad.ipa` into Sideloadly.
   - Enter your Apple ID email.
   - Click **Start** to sign and install the app onto your phone.
5. On your phone:
   - On iOS 16+, enable **Settings > Privacy & Security > Developer Mode** and reboot.
   - Go to **Settings > General > VPN & Device Management** and trust your Apple ID certificate.
   - Open OmniPad!

#### 3. Sideloading via AltStore / SideStore
1. Install [AltServer](https://altstore.io/) on Windows.
2. Install AltStore on your iPhone.
3. Transfer `OmniPad.ipa` to your iPhone (via AirDrop, iCloud Drive, or local Web download).
4. In AltStore on your phone, go to **My Apps**, tap `+`, and select `OmniPad.ipa`.

---

## 🌐 Network Protocol Specification

OmniPad utilizes a dual-path communication model:

1. **Control & Web UI (Port 27502 - HTTP / WebSocket)**:
   - Serves the customizable controller interface, skins, audio effects, and macros.
2. **Ultra-Low Latency Input Stream (Port 27500 - UDP)**:
   - Sends the 20-byte binary packet at up to **250 Hz**:
     - `Byte 0`: Magic Byte (`0xDA`)
     - `Byte 1`: Protocol Version (`0x01`)
     - `Byte 2`: Message Type (`0x01` = `MsgInput`)
     - `Byte 3`: Assigned Gamepad Slot (`0–3`, or `0xFF` during handshake)
     - `Bytes 4–7`: Sequence Number (`UInt32` Little-Endian)
     - `Bytes 8–9`: Digital Buttons Bitmask (`UInt16` Little-Endian)
     - `Byte 10`: Left Trigger (`UInt8`, 0–255)
     - `Byte 11`: Right Trigger (`UInt8`, 0–255)
     - `Bytes 12–13`: Left Thumbstick X (`Int16` Little-Endian, -32768 to 32767)
     - `Bytes 14–15`: Left Thumbstick Y (`Int16` Little-Endian, -32768 to 32767, positive is Up)
     - `Bytes 16–17`: Right Thumbstick X (`Int16` Little-Endian, -32768 to 32767)
     - `Bytes 18–19`: Right Thumbstick Y (`Int16` Little-Endian, -32768 to 32767, positive is Up)
3. **Bi-directional CoreHaptics Rumble (Port 27500 - UDP)**:
   - Server sends 6-byte packets: `[0xDA, 0x01, 0x05, Slot, LargeMotor, SmallMotor]`.
   - Handled instantly by `HapticEngine.swift` without passing through JavaScript.
4. **Auto-Discovery Broadcast (Port 27501 - UDP)**:
   - Probes `255.255.255.255:27501` with `[0xDA, 0x01, 0x06, 0xFF]`.
   - OmniPad PC Servers reply with machine hostname and listening web ports.

---

## 🔗 JavaScript Native Bridge (`window.OmniPadNative`)

The app injects a conformant `window.OmniPadNative` object into all frames:

| Method | Description |
|---|---|
| `vibrate(durationMs)` | Triggers transient haptic feedback for UI buttons |
| `vibrateHeavy(durationMs)` | Triggers heavy tactile punch for triggers or weapon recoil |
| `scanQrCode()` | Opens native camera scanner sheet |
| `scanAndSelectServer()` | Opens native Wi-Fi server discovery picker |
| `sendUdpInput(...)` | Directly feeds button and axis state to the 250 Hz UDP streaming engine |
| `sendBluetoothReport(...)` | Fallback routing to UDP streaming engine |
| `switchToBluetoothMode()` | Switches to local offline customizer view |

### Hardware Bumper Hook
Physical volume button actuation invokes the global JavaScript hook in real-time:
```javascript
window.omniPadTriggerButton('LB', true); // Volume Up pressed
window.omniPadTriggerButton('LB', false); // Volume Up released
window.omniPadTriggerButton('RB', true); // Volume Down pressed
window.omniPadTriggerButton('RB', false); // Volume Down released
```

---

## 🔒 Permissions & Privacy Considerations

- **Local Network (`NSLocalNetworkUsageDescription`)**:
  - **Required**: iOS requires user consent for an application to send UDP datagrams to other devices on the same Wi-Fi subnet.
- **Camera (`NSCameraUsageDescription`)**:
  - **Required for QR pairing**: Only accessed while the QR Scanner view is active.
- **Hardware Volume Buttons**:
  - Uses standard `AVAudioSession` output volume notification with a silent offscreen `MPVolumeView`. No special enterprise entitlements or private APIs are required, ensuring compliance with App Store and personal developer certificates.

---

## 💡 Troubleshooting

- **"Local Network Access" Prompt Didn't Appear**:
  - Check **Settings > Privacy & Security > Local Network > OmniPad** and verify it is turned **ON**.
- **Server Discovery Doesn't Find PC**:
  - Ensure your PC and iPhone are connected to the same Wi-Fi network (or that PC is on Ethernet connected to the same router).
  - Check Windows Firewall: allow incoming traffic for `OmniPadServer.App.exe` on UDP ports `27500`, `27501`, and TCP `27502`.
- **Volume Buttons Still Show Volume Bar**:
  - The silent `MPVolumeView` initializes as soon as the app starts. If the volume HUD briefly appears, toggle the "Bumpers" button in the OmniPad HUD to re-prime the audio session.
