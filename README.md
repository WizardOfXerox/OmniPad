# OmniPad - Universal Ultra-Low Latency Phone Gamepad

**OmniPad** turns any smartphone (Android or iOS) into a tournament-grade, ultra-low latency virtual game controller for PC.

It emulates a genuine **Microsoft Xbox 360 controller** (via kernel-level ViGEmBus) or **Sony DualShock 4**, and includes an automatic **Keyboard & Mouse fallback** for classic/strategy games that do not support controllers.

---

## ⚡ Latency & Transport Comparison

| Connection Mode | Latency | How It Works |
|---|:---:|---|
| **Wired USB** | **$\approx 0.5 - 1.5\text{ ms}$** | Automated ADB reverse port forward (`adb reverse tcp:27500 tcp:27500`). Zero Wi-Fi jitter. |
| **Wi-Fi 5GHz / 6** | **$\approx 2.0 - 4.5\text{ ms}$** | Binary 20-byte UDP stream at 125–250 Hz. Immune to TCP head-of-line blocking. |
| **Bluetooth HID** | **$\approx 4.0 - 8.0\text{ ms}$** | Direct Android 9+ `BluetoothHidDevice`. Pairs directly with Windows **without any server software on the PC**! |

---

## 🎮 Included Layout Presets

1. **Xbox 360 Standard**: Asymmetric dual sticks, D-Pad, ABXY, LB/RB bumpers, LT/RT analog triggers.
2. **PlayStation DualShock**: Symmetrical bottom sticks, shape buttons ($\triangle, \square, \bigcirc, \times$).
3. **Arcade Fightstick (Sanwa 8-Btn)**: Japanese Vewlix curved 8-button layout (LP/MP/HP/3P & LK/MK/HK/3K) + Sanwa LB-35 red ball-top joystick + Coin & 1P Start.
4. **Tekken Hitbox (8-Way Matrix)**: 8-way directional button matrix (cardinals + diagonals) + ergonomic arcade combo buttons.
5. **Racing Wheel & Pedals**: Gyroscope tilt steering + large vertical analog gas/brake pedals + paddle shifters.
6. **FPS Precision Aiming**: Left movement stick + capacitive touch aiming + gyro micro-aiming + hair triggers.
7. **Retro Arcade**: Classic D-Pad + A/B buttons for NES/SNES/Genesis emulators.
8. **Couch PC Trackpad & Media Remote**: Laptop trackpad surface + left/right click + media and Windows hotkeys.

---

## 🚀 Quick Start (Portable Mode)

No installation or framework dependencies required.

1. Open `OmniPad-Portable/` on your PC (or USB flash drive).
2. Double-click `OmniPadServer.exe`.
3. Point your smartphone camera at the **ASCII QR code** printed in the terminal.
4. The gamepad immediately launches in your phone's browser in fullscreen landscape mode!

---

## 🛠️ Advanced Features

* **Visual Canvas Customizer ("Edit Mode")**: Tap `📐 Edit` to drag, resize (50%–200%), and reposition any button or stick.
* **Timed Macro Sequencer**: Build millisecond-accurate combos (e.g. *Tekken* EWGF, shooter slide-cancels) with single-tap or hold-to-loop execution.
* **Simultaneous Multi-Button Combos**: Tap `+ Add Combo` in the editor to bind multiple buttons to one touch (e.g. $X+Y$, $A+B$).
* **Floating Dynamic Joysticks**: The joystick base dynamically anchors wherever your thumb first touches the screen.
* **AMOLED Pure Black Battery Saver**: True `#000000` background with glowing neon button outlines.
* **In-Game Force Feedback (Rumble)**: PC games send rumble motor feedback $\to$ server $\to$ phone vibrates dynamically.
* **Physical Bumper Buttons**: In the native Android app, hardware **Volume Up / Down** keys act as physical tactile **LB / RB** bumpers!

---

## 🧪 Testing & Verification

Run the automated test suite:
```powershell
# Unit tests for 20-byte packet serialization & sequence wrap math
dotnet test PCServer/OmniPadServer.Tests/OmniPadServer.Tests.csproj

# Protocol golden vector validation & 250 Hz latency stress test
python tools/fake_phone.py --selftest
python tools/fake_phone.py --rate 250 --duration 10
```
