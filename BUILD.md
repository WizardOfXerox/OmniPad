# OmniPad Developer & Contributor Build Guide

Welcome to the **OmniPad** build documentation. This guide walks you through compiling, testing, and packaging all components of OmniPad from source code.

---

## 🏛️ Project Architecture Overview

OmniPad consists of three tightly coupled components designed for zero-latency, high-frequency gamepad emulation:

```
OmniPad/
├── PCServer/                   # High-performance .NET 8 C# host server
│   ├── OmniPadServer.App/      # Kestrel Web, WebSocket, UDP engine, & CLI terminal UI
│   ├── OmniPadServer.Core/     # Binary protocol parser (20B packet), session & slot manager
│   ├── OmniPadServer.ViGEm/    # Kernel-level virtual gamepad driver (Xbox 360 / DS4) & KBM fallback
│   ├── OmniPadServer.Tests/    # 56 automated xUnit tests (packet math, gyro filters, resilience)
│   ├── OmniPadServer.Legacinator/ # Driver hygiene and ghost device cleanup utility
│   └── OmniPadUpdater.App/     # Delta updater and manifest verification utility
├── AndroidClient/              # Native Android application (Kotlin, SDK 35, Min SDK 21)
│   └── app/src/main/           # Hardware buttons (volume bumpers), Bluetooth HID, QR scanner
├── iOSClient/                  # Native iOS application (SwiftUI, iOS 15.0+, Network.framework, CoreHaptics)
│   ├── OmniPad.xcodeproj/      # Xcode project for iOS build & sideloading
│   └── OmniPad/                # Hardware volume bumpers, 250 Hz UDP streaming, QR scanner, JS bridge
├── WebClient/                  # Zero-install HTML5/Canvas progressive web app (PWA)
│   ├── js/                     # Touch engine, micro-aiming gyro filter, layout editor, macros
│   └── css/                    # Low-power OLED pure-black themes & responsive styling
├── OmniPad-Portable/           # Zero-install portable bundle directory for instant deployment
├── tools/                      # Validation tools (fake_phone.py 250 Hz stress tester)
├── build.ps1                   # Universal PowerShell build automation pipeline
└── build.bat                   # Universal Command Prompt batch wrapper
```

---

## 📋 Prerequisites & Toolchain Setup

To build the entire project locally, ensure you have the following installed:

| Tool | Version Required | Purpose |
|---|:---:|---|
| **Operating System** | Windows 10 / 11 (x64), Linux (Ubuntu, Debian, Fedora, Arch, SteamOS), or macOS 12+ | Host OS for running PCServer |
| **.NET SDK** | `8.0.100` or higher | Compiles `PCServer` and runs unit tests |
| **Java Development Kit** | **JDK 17 or JDK 21** | Required for Android Gradle builds (JDK 25 is unsupported by Gradle) |
| **Android SDK** | API 35 (Build-Tools 35.0.0) | Compiles `AndroidClient` APK |
| **Xcode / Command Line Tools** | Xcode 14, 15, or 16+ | Compiles and archives `iOSClient` IPA on macOS |
| **Gamepad Driver (Windows)** | ViGEmBus v1.22.0+ WHQL *(Optional)* | Virtual Xbox/DS4 controllers. *(Fallback: Keyboard/Mouse)* |
| **Gamepad Driver (Linux)** | `/dev/uinput` (Kernel module) | Native virtual Xbox/DS4 controllers and mouse/keyboard emulation |
| **Python** *(Optional)* | 3.8+ | For running `tools/fake_phone.py` protocol stress tests |

> [!TIP]
> On Linux, zero third-party drivers are required. OmniPad leverages native `/dev/uinput` to instantiate high-performance virtual Xbox 360 and DualShock 4 gamepads directly into the Linux evdev/input subsystem. On macOS, CoreGraphics APIs provide native mouse and keyboard simulation.

---

## 🚀 One-Click Automated Build

### Linux & macOS (Bash)
We provide `./build.sh` for one-click compilation, testing, and packaging on Linux and macOS:

```bash
# Make executable (if needed)
chmod +x build.sh

# Build PC Server and run unit tests (auto-detects Linux vs macOS Darwin)
./build.sh

# Build and package into release archive (.tar.gz)
./build.sh --package

# Skip tests for rapid iteration
./build.sh --skip-tests
```

### Windows (PowerShell & Command Prompt)
We provide unified build scripts (`build.bat` and `build.ps1`) that automate the entire workflow:
1. Synchronizing WebClient assets to all targets (Web server static root & Android offline assets).
2. Building `PCServer.sln` under Release configuration.
3. Executing the automated unit test suite.
4. Publishing a single-file, self-contained `OmniPadServer.exe` (or Linux x64 binary via `-Runtime linux-x64`).
5. Compiling the Android `OmniPad.apk` debug package.
6. *(Optional)* Packaging into `OmniPad-Portable.zip` or `OmniPad-Linux-x64.tar.gz`.

**From PowerShell (`pwsh` or `powershell`):**
```powershell
# Default Windows build
.\build.ps1
.\build.ps1 -SkipAndroid
.\build.ps1 -Package

# Cross-compile Linux x64 single-file bundle from Windows:
.\build.ps1 -Runtime linux-x64 -SkipAndroid -Package

# Build both Windows and Linux release packages:
.\build.ps1 -Runtime all -Package
```

**From Command Prompt (`cmd.exe`):**
```cmd
:: Build everything (PC Server, Tests, and Android APK)
build.bat

:: Build only PC Server and WebClient (skips Android SDK requirements)
build.bat -SkipAndroid

:: Build and immediately package into a release ZIP
build.bat -Package
```

---

## 🔧 Manual Step-by-Step Build Instructions

If you prefer building individual sub-projects manually, follow the sections below:

### 1. Synchronize WebClient Assets
The WebClient frontend is shared between the PC Server's embedded Kestrel web server and the native Android WebView offline bundle.

Run the following in PowerShell whenever modifying files in `WebClient/`:
```powershell
# Copy WebClient into PCServer portable web roots
Copy-Item -Path "WebClient\*" -Destination "OmniPad-Portable\WebClient\" -Recurse -Force
Copy-Item -Path "WebClient\*" -Destination "OmniPad-Portable\wwwroot\" -Recurse -Force

# Copy WebClient into Android offline assets
Copy-Item -Path "WebClient\*" -Destination "AndroidClient\app\src\main\assets\web\" -Recurse -Force
```

---

### 2. Building the PC Server (.NET 8)

1. **Restore NuGet Packages**:
   ```powershell
   dotnet restore PCServer/OmniPadServer.sln
   ```

2. **Compile Release Binaries**:
   ```powershell
   dotnet build PCServer/OmniPadServer.sln -c Release
   ```

3. **Run Unit Tests**:
   ```powershell
   dotnet test PCServer/OmniPadServer.Tests/OmniPadServer.Tests.csproj -c Release --verbosity normal
   ```
   *Expected result: 56/56 tests passing.*

4. **Publish Self-Contained Win-x64 Executable**:
   ```powershell
   dotnet publish PCServer/OmniPadServer.App/OmniPadServer.App.csproj `
       -c Release `
       -r win-x64 `
       --self-contained true `
       -p:PublishSingleFile=true `
       -o OmniPad-Portable
   ```
   Ensure the output executable is named `OmniPadServer.exe`:
   ```powershell
   Copy-Item "OmniPad-Portable\OmniPadServer.App.exe" "OmniPad-Portable\OmniPadServer.exe" -Force
   ```

---

### 3. Building the Android Client (Kotlin / Gradle)

1. **Set `JAVA_HOME` to JDK 17 or 21**:
   ```powershell
   $env:JAVA_HOME = "C:\Program Files\Android\Android Studio\jbr"
   $env:PATH = "$env:JAVA_HOME\bin;$env:PATH"
   ```

2. **Compile Debug APK**:
   ```powershell
   cd AndroidClient
   .\gradlew.bat assembleDebug --no-daemon
   ```

3. **Locate Compiled APK**:
   The output APK is generated at:
   ```
   AndroidClient/app/build/outputs/apk/debug/app-debug.apk
   ```

4. **Install to Device via ADB**:
   ```powershell
   adb install -r app/build/outputs/apk/debug/app-debug.apk
   ```

---

## 🧪 Testing & Validation Harness

OmniPad includes automated testing tools to verify network integrity, sequence wrapping, and driver stability:

### 1. Unit Tests (xUnit)
```powershell
dotnet test PCServer/OmniPadServer.Tests/OmniPadServer.Tests.csproj -c Release
```
Covers:
* **GyroMathTests**: Auto-rest bias calibration, micro-tremor suppression, flick stick deflection, and shake gesture recognition.
* **ProtocolTests**: 20-byte binary packet serialization, golden vector round-trips, and corrupt packet rejection.
* **SessionManagerTests**: Multi-client slot arbitration (1–4), sequence wrap-around math ($65535 \to 0$), and ephemeral port session reclamation.
* **TouchpadAndDriverResilienceTests**: 12-bit coordinate packing, DS4 touchpad report synthesis, and driver disconnect fallbacks.
* **DsuProtocolTests**: DSU / Cemuhook protocol versioning and CRC-32 verification for Cemu / Dolphin / Yuzu / Ryujinx motion support.

### 2. High-Frequency Protocol & Latency Stress Test
Simulates a phone streaming 20-byte input packets at up to 250 Hz:
```powershell
# Verify protocol packet vector matches C# server spec exactly
python tools/fake_phone.py --selftest

# Stream packets at 250 Hz for 10 seconds to measure latency & packet loss
python tools/fake_phone.py --rate 250 --duration 10
```

---

## 🌐 Network Ports & Firewall Rules

OmniPad utilizes the following default ports:

| Port | Protocol | Purpose |
|---|:---:|---|
| **27500** | UDP | High-speed binary gamepad input stream (20B packets) |
| **27501** | UDP | LAN auto-discovery broadcast |
| **27502** | TCP / HTTP | Embedded web server & WebSocket real-time channel |
| **27503** | TCP | Low-latency audio / microphone capture stream |
| **26760** | UDP | DSU / Cemuhook motion server for emulator integration |

If connecting over Wi-Fi, ensure Windows Defender Firewall allows traffic through `OmniPadServer.exe`.

---

## 🛠️ Troubleshooting & FAQs

### Q1: `FAILURE: Build failed with an exception. * What went wrong: 25.0.4`
**Cause**: Gradle 8.10 does not yet support Java 25.  
**Fix**: Ensure your `JAVA_HOME` points to JDK 17 or JDK 21 (e.g. Android Studio JBR at `C:\Program Files\Android\Android Studio\jbr`). Running `build.bat` or `build.ps1` automatically detects and selects a compatible installed JDK.

### Q2: `VigemBusNotFoundException` or "ViGEmBus driver not installed"
**Cause**: The PC does not have the ViGEmBus driver installed.  
**Resolution**:
* OmniPad will automatically fall back to **Keyboard & Mouse (`SendInput`) mode**, allowing gameplay without any drivers.
* To enable full Xbox 360 / DualShock 4 emulation, download and install the official WHQL driver from [ViGEmBus GitHub Releases](https://github.com/nefarius/ViGEmBus/releases).

### Q3: How to achieve sub-millisecond latency over USB?
Enable USB Debugging on your phone, connect via USB cable, and run:
```powershell
adb reverse tcp:27500 tcp:27500
adb reverse tcp:27502 tcp:27502
```
Then navigate to `http://localhost:27502` on your phone browser.

### Q4: Linux `/dev/uinput` Permission Denied
**Cause**: By default, `/dev/uinput` requires root permissions or membership in the `input` group.  
**Resolution**: Run the following commands once to allow standard user access:
```bash
sudo usermod -aG input $USER
echo 'KERNEL=="uinput", MODE="0660", GROUP="input", OPTIONS+="static_node=uinput"' | sudo tee /etc/udev/rules.d/99-omnipad-uinput.rules
sudo udevadm control --reload-rules && sudo udevadm trigger
```
Log out and log back in (or restart) for group membership to take effect. If `/dev/uinput` is still not accessible, OmniPadServer automatically falls back to virtual keyboard/mouse emulation.
