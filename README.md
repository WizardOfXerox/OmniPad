# OmniPad - Universal Ultra-Low Latency Phone Gamepad

[![CI Build](https://github.com/WizardOfXerox/OmniPad/actions/workflows/ci.yml/badge.svg)](https://github.com/WizardOfXerox/OmniPad/actions/workflows/ci.yml)
[![Latest Release](https://img.shields.io/github/v/release/WizardOfXerox/OmniPad?color=blue&label=release)](https://github.com/WizardOfXerox/OmniPad/releases/latest)
[![Platforms](https://img.shields.io/badge/platforms-Windows%20%7C%20Linux%20%7C%20macOS%20%7C%20Android%20%7C%20iOS-orange.svg)](#-supported-platforms-matrix)
[![.NET 8.0](https://img.shields.io/badge/.NET-8.0-512BD4.svg?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/download/dotnet/8.0)
[![Android](https://img.shields.io/badge/Android-API%2021--35-3DDC84.svg?logo=android&logoColor=white)](https://developer.android.com)
[![iOS](https://img.shields.io/badge/iOS-15.0%2B-000000.svg?logo=apple&logoColor=white)](https://developer.apple.com/ios/)
[![ViGEmBus](https://img.shields.io/badge/Driver-ViGEmBus%20WHQL-0078D6.svg)](https://github.com/nefarius/ViGEmBus)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

**OmniPad** turns any smartphone or tablet (**Android** or **iOS**) into a tournament-grade, ultra-low latency virtual game controller for PC (**Windows**, **Linux**, and **macOS**).

It emulates a genuine **Microsoft Xbox 360 controller** (via kernel-level ViGEmBus on Windows, native `/dev/uinput` on Linux), **Sony DualShock 4**, and **macOS CoreGraphics input simulation**, alongside native **DSU / Cemuhook motion gyro** support and an automatic **Keyboard & Mouse fallback** for classic/strategy games.

---

## 📦 Official Release Downloads (v1.4.0)

Pre-compiled, self-contained binaries are published and ready to use from the [**GitHub Releases**](https://github.com/WizardOfXerox/OmniPad/releases/tag/v1.4.0) page:

| Platform | Package | Size | Description |
|---|---|:---:|---|
| 🪟 **Windows Host** | [**`OmniPad-Portable.zip`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad-Portable.zip) | 262 MB | Complete zero-install Windows server (`win-x64`), WebClient bundle, Updater, and Legacinator driver utility. |
| 🐧 **Linux Host** | [**`OmniPad-Linux-x64.tar.gz`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad-Linux-x64.tar.gz) | 114 MB | Self-contained Linux host server (`linux-x64`) with native `/dev/uinput` Xbox 360 / DS4 kernel emulation. |
| 🍎 **macOS Host** | [**`OmniPad-macOS.tar.gz`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad-macOS.tar.gz) | 142 MB | Universal macOS host package with auto-sensing launcher for Apple Silicon (`arm64`) & Intel (`x64`). |
| 📱 **Android Client** | [**`OmniPad.apk`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad.apk) | 4.7 MB | Standalone Android client APK with hardware volume bumpers, Bluetooth HID, low-latency UDP, and QR scanner. |
| 🍏 **iOS Client** | [**`OmniPad.ipa`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad.ipa) | 786 KB | Sideloadable iOS client IPA for AltStore, SideStore, Sideloadly, or TrollStore with CoreHaptics rumble and volume bumpers. |

---

## 🗺️ Supported Platforms Matrix

```
                          ┌──────────────────────────┐
                          │   Mobile Clients         │
                          │   • Android (Native/Web) │
                          │   • iOS (Native/Web)     │
                          └─────────────┬────────────┘
                                        │
           ┌────────────────────────────┼────────────────────────────┐
           │ 20-Byte Binary UDP (250Hz) │ Zero-Jitter USB (ADB)      │ Bluetooth HID (Direct)
           ▼                            ▼                            ▼
┌──────────────────────┐     ┌──────────────────────┐     ┌──────────────────────┐
│ 🪟 Windows Host      │     │ 🐧 Linux Host        │     │ 🍎 macOS Host        │
│ • ViGEmBus (X360/DS4)│     │ • /dev/uinput        │     │ • CoreGraphics       │
│ • DSU Motion Server  │     │ • DSU Motion Server  │     │ • DSU Motion Server  │
│ • SendInput KBM      │     │ • Uinput KBM         │     │ • KBM Simulation     │
└──────────────────────┘     └──────────────────────┘     └──────────────────────┘
```

### Host Operating Systems (PC Server)
* **Windows**: Windows 10 & 11 (64-bit and 32-bit). Kernel-level virtual Xbox 360 and DualShock 4 controllers via ViGEmBus, low-latency WASAPI audio capture, and DXGI screen streaming.
* **Linux**: Ubuntu, Debian, Fedora, Arch Linux, SteamOS (Steam Deck). Zero drivers required; uses native Linux `/dev/uinput` for hardware-level controller, mouse, and keyboard creation.
* **macOS**: macOS 12+ Monterey, Ventura, Sonoma, and Sequoia. Native universal binaries for Apple Silicon (M1–M4) and Intel x64. Emulates mouse and keyboard via CoreGraphics `CGEvent` APIs.
* **Emulators (All Hosts)**: Integrated DSU / Cemuhook motion server on UDP port 26760 for full gyro aiming in Cemu, Dolphin, Ryujinx, Yuzu, and RPCS3.

### Client Operating Systems (Controller)
* **Android**: Native app for Android 5.0 to 15+ (API 21–35). Includes hardware volume bumpers, Bluetooth HID mode, dual motor haptics, and camera QR scanning.
* **iOS / iPadOS**: Native app for iOS 15.0 to 18+. Built with SwiftUI, `Network.framework` 250 Hz UDP, Apple `CoreHaptics` dual-motor rumble, and physical volume bumpers.
* **Zero-Install WebClient**: Works in any modern mobile browser (Chrome, Safari, Firefox, Edge) with zero app installation.

---

## ⚡ Latency & Transport Comparison

| Connection Mode | Latency | How It Works |
|---|:---:|---|
| **Wired USB** | **$\approx 0.5 - 1.5\text{ ms}$** | Automated ADB reverse port forward (`adb reverse tcp:27500 tcp:27500`). Zero Wi-Fi jitter. |
| **Wi-Fi 5GHz / 6** | **$\approx 2.0 - 4.5\text{ ms}$** | Binary 20-byte UDP stream at 125–250 Hz. Immune to TCP head-of-line blocking. |
| **Bluetooth HID** | **$\approx 4.0 - 8.0\text{ ms}$** | Direct Android 9+ `BluetoothHidDevice`. Pairs directly with Windows **without any server software on the PC**! |

---

## 🚀 Quick Start Guides

### 1. 🪟 Windows Host (Zero-Install Portable)
1. Download and extract [**`OmniPad-Portable.zip`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad-Portable.zip).
2. Double-click `OmniPadServer.exe`.
3. If prompted by Windows Defender Firewall, allow Private/Public network access.
4. Scan the ASCII QR code printed in the console using your phone camera (or open the displayed URL in your browser).
> [!TIP]
> For genuine Xbox 360 / DualShock 4 emulation, install the [ViGEmBus driver](https://github.com/nefarius/ViGEmBus/releases). If ViGEmBus is not installed, OmniPad automatically falls back to Keyboard & Mouse mode!

---

### 2. 🐧 Linux Host (Ubuntu, Arch, Fedora, SteamOS)
1. Download and extract [**`OmniPad-Linux-x64.tar.gz`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad-Linux-x64.tar.gz):
   ```bash
   tar -xzf OmniPad-Linux-x64.tar.gz
   cd OmniPad-Linux-x64
   ```
2. Grant `/dev/uinput` permissions to your user account (run once):
   ```bash
   sudo usermod -aG input $USER
   echo 'KERNEL=="uinput", MODE="0660", GROUP="input", OPTIONS+="static_node=uinput"' | sudo tee /etc/udev/rules.d/99-omnipad-uinput.rules
   sudo udevadm control --reload-rules && sudo udevadm trigger
   ```
3. Run the server:
   ```bash
   chmod +x OmniPadServer
   ./OmniPadServer
   ```
4. Point your phone to the displayed local IP and port (e.g. `http://192.168.1.50:27502`).

---

### 3. 🍎 macOS Host (Apple Silicon & Intel)
1. Download and extract [**`OmniPad-macOS.tar.gz`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad-macOS.tar.gz):
   ```bash
   tar -xzf OmniPad-macOS.tar.gz
   cd OmniPad-macOS
   ```
2. Run the universal launcher:
   ```bash
   chmod +x OmniPadServer
   ./OmniPadServer
   ```
3. **Accessibility Permission**:
   - Go to **System Settings > Privacy & Security > Accessibility**.
   - Enable your terminal app (Terminal, iTerm2, etc.) or `OmniPadServer` to allow CoreGraphics mouse and keyboard simulation.
4. Connect from your phone via browser or the iOS/Android native app.

---

### 4. 📱 Android Client
* **Native App (Recommended)**: Download and install [**`OmniPad.apk`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad.apk).
  - Open the app, tap the QR icon, and scan the terminal QR code.
  - Enjoy hardware **Volume Up / Down** keys as tactile **LB / RB** bumpers, 250 Hz UDP streaming, and Bluetooth HID mode.
* **Instant Web Browser**: Open Google Chrome or Firefox on your phone and navigate to `http://<PC-IP>:27502`.

---

### 5. 🍏 iOS Client (iPhone & iPad)
* **Native Sideloading**:
  1. Download [**`OmniPad.ipa`**](https://github.com/WizardOfXerox/OmniPad/releases/download/v1.4.0/OmniPad.ipa).
  2. Sideload using **Sideloadly**, **AltStore**, **SideStore**, or **TrollStore**:
     - Connect iPhone via USB $\to$ Drag `OmniPad.ipa` into Sideloadly $\to$ Enter Apple ID $\to$ Click Start.
  3. On your iPhone: Go to **Settings > General > VPN & Device Management** and trust your developer certificate.
  4. Launch OmniPad and tap **Allow** when prompted for **Local Network** access.
  5. Features Apple `CoreHaptics` dual-motor rumble and physical volume bumpers!
* **Instant Web Browser**: Open Mobile Safari and navigate to `http://<PC-IP>:27502`. Add to Home Screen for fullscreen landscape gaming.

---

## 🎮 Included Layout Presets

1. **Xbox 360 Standard**: Asymmetric dual sticks, D-Pad, ABXY, LB/RB bumpers, LT/RT analog triggers.
2. **PlayStation DualShock**: Symmetrical bottom sticks, shape buttons ($\triangle, \square, \bigcirc, \times$), clickable touch pad.
3. **Arcade Fightstick (Sanwa 8-Btn)**: Japanese Vewlix curved 8-button layout (LP/MP/HP/3P & LK/MK/HK/3K) + Sanwa LB-35 red ball-top joystick + Coin & 1P Start.
4. **Tekken Hitbox (8-Way Matrix)**: 8-way directional button matrix (cardinals + diagonals) + ergonomic arcade combo buttons.
5. **Racing Wheel & Pedals**: Gyroscope tilt steering + large vertical analog gas/brake pedals + paddle shifters.
6. **FPS Precision Aiming**: Left movement stick + capacitive touch aiming + gyro micro-aiming + hair triggers.
7. **Retro Arcade**: Classic D-Pad + A/B buttons for NES/SNES/Genesis emulators.
8. **Couch PC Trackpad & Media Remote**: Laptop trackpad surface + left/right click + media and Windows hotkeys.

---

## 🛠️ Advanced Features

* **Visual Canvas Customizer ("Edit Mode")**: Tap `📐 Edit` to drag, resize (50%–200%), and reposition any button or stick.
* **Timed Macro Sequencer**: Build millisecond-accurate combos (e.g. *Tekken* EWGF, shooter slide-cancels) with single-tap or hold-to-loop execution.
* **Simultaneous Multi-Button Combos**: Tap `+ Add Combo` in the editor to bind multiple buttons to one touch (e.g. $X+Y$, $A+B$).
* **Floating Dynamic Joysticks**: The joystick base dynamically anchors wherever your thumb first touches the screen.
* **AMOLED Pure Black Battery Saver**: True `#000000` background with glowing neon button outlines.
* **In-Game Force Feedback (Rumble)**: PC games send rumble motor feedback $\to$ server $\to$ phone vibrates dynamically (CoreHaptics on iOS, dual vibrator on Android).
* **Physical Bumper Buttons**: In the native Android & iOS apps, hardware **Volume Up / Down** keys act as physical tactile **LB / RB** bumpers!
* **DSU / Cemuhook Motion Protocol**: Built-in motion server on UDP port 26760 feeds real phone gyroscope/accelerometer data into Nintendo Switch, Wii U, and PS3 emulators (Cemu, Dolphin, Ryujinx, Yuzu).

---

## 🌐 Network Ports & Protocol

| Port | Protocol | Purpose |
|---|:---:|---|
| **27500** | UDP | High-speed binary gamepad input stream (20-byte frames at 125–250 Hz) |
| **27501** | UDP | LAN auto-discovery broadcast |
| **27502** | TCP / HTTP | Embedded web server & WebSocket real-time channel |
| **27503** | TCP | Low-latency audio / microphone capture stream |
| **26760** | UDP | DSU / Cemuhook motion server for emulator integration |

---

## 🏗️ Building from Source

OmniPad provides automated, one-click build scripts for every major operating system:

### 🪟 Windows (`build.bat` / `build.ps1`)
```cmd
:: Build everything (PC Server, unit tests, and Android APK)
build.bat

:: Build only PC Server & WebClient (skips Android SDK requirements)
build.bat -SkipAndroid

:: Package into a release-ready OmniPad-Portable.zip
build.bat -Package
```

### 🐧 Linux (`build.sh`)
```bash
chmod +x build.sh
# Build server and run unit tests
./build.sh

# Build and package into OmniPad-Linux-x64.tar.gz
./build.sh --package
```

### 🍎 macOS (`build.sh` / `build.ps1`)
```bash
# Build native macOS host server
./build.sh

# Or cross-compile universal bundle via PowerShell:
pwsh ./build.ps1 -Runtime osx-universal -Package
```

### 🍏 iOS Client (Xcode)
```bash
cd iOSClient
xcodebuild clean archive \
  -project OmniPad.xcodeproj \
  -scheme OmniPad \
  -archivePath build/OmniPad.xcarchive \
  -configuration Release \
  CODE_SIGNING_ALLOWED=NO
```

### 📱 Android Client (Gradle)
```bash
cd AndroidClient
./gradlew assembleRelease
```

For complete step-by-step developer setup, toolchain requirements, Linux `/dev/uinput` configuration, manual compilation instructions, and troubleshooting guides, see **[BUILD.md](BUILD.md)**.

---

## 🧪 Testing & Verification

OmniPad includes an extensive automated test harness to verify network integrity, sequence wrapping, and driver stability:

```bash
# Unit test suite (56/56 passing on Windows, Linux, and macOS)
dotnet test PCServer/OmniPadServer.Tests/OmniPadServer.Tests.csproj

# Protocol golden vector validation & 250 Hz latency stress tester
python tools/fake_phone.py --selftest
python tools/fake_phone.py --rate 250 --duration 10
```

---

## 📄 License & Open-Source Attribution

OmniPad is open-source under the [MIT License](LICENSE).  
* Windows driver support powered by the [ViGEm.NET](https://github.com/nefarius/ViGEm.NET) library and the [ViGEmBus](https://github.com/nefarius/ViGEmBus) kernel-mode driver by Benjamin Höglinger-Stelzer (Nefarius).
* Linux driver support powered by the native Linux kernel `uinput` subsystem.
* macOS simulation powered by Apple `CoreGraphics` `CGEvent` services.
