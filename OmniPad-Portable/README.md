# OmniPad Portable - Setup & User Guide

Welcome to **OmniPad Portable**! This folder contains a completely self-contained, zero-install distribution of the OmniPad PC Server, drivers, tools, and Android client APK.

With OmniPad, you can turn any smartphone or tablet (Android or iOS) into a tournament-grade, ultra-low-latency game controller, virtual mouse trackpad, and gaming keyboard for your PC.

---

## 📁 Package Contents

| File / Folder | Purpose |
|---|---|
| `OmniPadServer.exe` | **Standalone Zero-Install Server** — includes bundled .NET 8 runtime. Runs on ANY Windows 10/11 PC with zero prerequisites. |
| `OmniPadServer.App.exe` | **Modular PC Server** — lightweight executable that runs the input engine, web server, and device bridges. |
| `install_all_prerequisites.bat` | **One-Click Master Installer** — checks & installs ViGEmBus controller driver, .NET 8 runtime, and Windows Firewall rules. |
| `install_vigem.bat` | One-click installer for the **ViGEmBus** Xbox 360/PS4 controller driver. |
| `install_dotnet.bat` | One-click installer for the official **Microsoft .NET 8 Desktop Runtime** (if running on a clean PC). |
| `uninstall_vigem.bat` | Removes the ViGEmBus driver. |
| `setup_driver.ps1` | PowerShell script for automated environment & driver setup & verification. |
| `legacinator.bat` | Driver cleaner tool (`OmniPadLegacinator`) to detect and remove conflicting legacy drivers. |
| `apks/OmniPad-latest.apk` | Ready-to-install Android client APK for your phone or tablet (with camera QR scanner). |
| `WebClient/` & `wwwroot/` | The web application files served to mobile browsers. |

---

## ⚡ Quick Setup (3 Easy Steps)

### Step 1: Install the Controller Driver (One-Time Only)

OmniPad emulates genuine **Microsoft Xbox 360** and **Sony DualShock 4** controllers at the Windows kernel level. For Windows and games to recognize the virtual controllers, the **ViGEmBus** driver must be installed:

1. Right-click **`install_vigem.bat`** and select **Run as administrator**.
2. Follow the prompt to complete the installation (takes under 10 seconds).
3. *(Optional)* If you previously had old or conflicting controller drivers (such as ScpToolkit or very old ViGEm versions), right-click **`legacinator.bat`** and run as administrator to clean them up.

---

### Step 2: Install the App on Your Phone or Tablet

Choose the method that suits your device:

#### Option A: Native Android App (Recommended)
- Copy **`apks/OmniPad-latest.apk`** to your Android device (or download it directly to your phone).
- Open the APK on your device to install it (grant "Install Unknown Apps" permission if prompted).
- **Advantages**: Lowest input latency, hardware Volume Up/Down buttons function as physical tactile **LB / RB** bumpers, and haptic feedback.

#### Option B: Browser / PWA (Zero-Install — Android, iOS, iPadOS)
- You do **not** need to install any app. Simply connect using Chrome, Safari, or Edge on your mobile device as described in Step 3.
- On iOS / iPadOS, tap **Share** $\to$ **Add to Home Screen** for full-screen borderless play.

---

### Step 3: Launch the PC Server & Connect

1. Double-click **`OmniPadServer.App.exe`** (or `OmniPadServer.exe`).
2. If the Windows Defender Firewall prompt appears, check **Private networks** and click **Allow access**.
3. The server console will open and display an ASCII **QR Code** and your PC's connection URL (e.g., `http://192.168.x.x:27502`).
4. **Connect your phone**:
   - **Using the Camera / Browser**: Point your phone camera at the QR code in the terminal to open the gamepad instantly.
   - **Using the Android App**: Open the OmniPad app; it will automatically discover and connect to your PC on the local network.

---

## 🎮 How It Works & Core Features

### 1. Gamepad Emulation (Xbox 360 & PS4)
- When your phone connects, the server attaches a virtual Xbox 360 controller via the ViGEm kernel bus.
- Windows and all games (Steam, Epic, Game Pass, emulators, etc.) see it as an authentic physical controller.
- Supports up to **16 simultaneous players** for local multiplayer parties!

### 2. Zero-Driver Virtual Mouse Trackpad
Need to control the Windows desktop, click through game launchers, or browse the web from your couch?
- Tap the **Mouse icon** in the top HUD dock.
- **1-Finger Drag**: Smooth, high-precision cursor movement.
- **1-Finger Tap**: Left click.
- **2-Finger Tap**: Right click.
- **2-Finger Slide**: Mouse scroll wheel.
- **Bottom Buttons**: Dedicated hardware-style `LEFT CLICK` and `RIGHT CLICK` buttons.
- Fully coexists with your physical PC mouse.

### 3. Zero-Driver Virtual Gaming Keyboard
Want to type in game chat, enter cheat codes, or play keyboard-only PC games (like *Tekken 7*, FPS titles, or RPGs)?
- Tap the **Keyboard icon** in the top HUD dock.
- High-contrast gaming keyboard drawer with highlighted **W, A, S, D** cluster.
- **DirectX Hardware Scan Code Mapping**: Uses Windows `MapVirtualKeyW` so fullscreen games that read raw scan codes (*Tekken 7*, Unreal Engine, Unity, Steam games) receive keys accurately without dropping inputs.
- **Multi-Touch Support**: Hold W to run forward while tapping Space to jump and Shift to sprint simultaneously.
- Leak-safe input tracking ensures no keys get stuck held down.

### 4. Game Background Screen Streaming
- Tap the **Screen icon** in the HUD dock.
- Streams your PC's desktop or game video directly onto your gamepad background at 60 FPS with low latency.

### 5. Headphone Jack & Audio Streaming
- Tap the **Headphone icon** to stream game audio from your PC directly to your phone's speaker or headphones.

### 6. Wireless Microphone
- Tap the **Microphone icon** to use your phone's built-in microphone as a wireless PC microphone for Discord or in-game voice chat.

### 7. Multi-Player Slot Switching
- Tap the **`P1 ⇄`** pill in the top HUD.
- Instantly switch controller slots (P1 through P16) or swap controller numbers with friends without disconnecting.

### 8. DSU Motion / Gyroscope Aiming
- The server broadcasts 6-axis motion data on UDP port `26760` (CemuHook / DSU protocol).
- Compatible with **Cemu**, **Dolphin**, **Ryujinx**, **Yuzu**, and **RPCS3** for motion controls and gyro aiming.

---

## 🔌 Connection Modes & Latency

| Connection Mode | Typical Latency | Notes |
|---|:---:|---|
| **USB Cable (Tethered)** | **$\approx 0.5 - 1.5\text{ ms}$** | Best for competitive fighting games & rhythm games. Uses automated ADB reverse port forwarding. |
| **Wi-Fi (5GHz / Wi-Fi 6)** | **$\approx 2.0 - 5.0\text{ ms}$** | Recommended wireless setup. Fast, cable-free, low jitter. |
| **Wi-Fi (2.4GHz)** | **$\approx 8.0 - 16.0\text{ ms}$** | Works on any Wi-Fi router. Keep close to the access point to reduce packet loss. |

### Using Wired USB Mode (Ultra-Low Latency)
1. Connect your Android device to your PC with a USB cable.
2. Enable **USB Debugging** under *Developer Options* on your phone.
3. The server automatically detects the device and forwards input over USB (`adb reverse tcp:27500 tcp:27500`).
4. On your phone, navigate to `http://127.0.0.1:27502`.

---

## ❓ Troubleshooting & FAQs

#### Q: The game doesn't recognize the controller.
- Ensure the ViGEmBus driver is installed by running `install_vigem.bat` as administrator.
- Check Windows *Game Controllers* (`joy.cpl`) to verify that "Controller (XBOX 360 For Windows)" appears.

#### Q: The phone says "Connecting..." and doesn't connect.
- Ensure your phone and PC are connected to the same Wi-Fi network.
- Verify Windows Firewall is not blocking port `27502` (TCP) and `27500` (UDP).
- If your router has "AP Isolation" or "Guest Mode" enabled, devices cannot communicate with each other; disable AP Isolation in router settings.

#### Q: Keystrokes work in Notepad but not in my full-screen game.
- Some full-screen games require administrator privileges to receive simulated input. Right-click `OmniPadServer.App.exe` and select **Run as administrator**.

#### Q: How do I turn off the server?
- Press `Ctrl + C` in the server terminal window, or simply close the window.

---

*Enjoy playing with OmniPad!*
