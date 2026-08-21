# Advanced Controller Processor

**PS5 DualSense → Virtual Xbox 360 / DualShock 4 controller processor for Windows**

Transform your DualSense controller input with advanced processing — deadzones, response curves, speed multipliers, directional speeds and smoothing — then feed it into your games as a fully working virtual Xbox 360 or DualShock 4 controller.

> ⚠️ **Closed-source software.** This repository hosts ready-to-run releases only. No source code is published here.

---

## ✨ Features

- 🎮 **DualSense support** — USB & Bluetooth
- 🕹️ **Per-stick processing** — Deadzone, Response Curves (Linear / Soft / Aggressive / Custom)
- ⚡ **Speed multipliers** — independent X/Y axis scaling
- 🧭 **Directional speed** — separate forward/backward/left/right tuning
- 🌊 **Input smoothing**
- 👀 **Live monitor** — real-time raw vs processed stick visualization
- 💾 **Profiles** — save / load / export / import
- ⌨️ **Hotkeys** — F8 toggles processing · F9 safe-mode reset
- 🎯 **Virtual output** — Xbox 360 (XInput) or DualShock 4

---

## ⬇️ Download & Run

1. Open the [**Releases**](../../releases/latest) page
2. Download **`AdvancedControllerProcessor.exe`**
3. Double-click to run — **no installation needed**, the .NET runtime is embedded inside the file

### 🛡️ Windows SmartScreen warning

The executable is not code-signed, so Windows may show *"Windows protected your PC"*.
Click **More info → Run anyway**. This is normal for unsigned indie software.

---

## 🔧 Requirements

| Requirement | Notes |
|-------------|-------|
| Windows 10 / 11 (64-bit) | Required |
| [ViGEmBus driver](https://vigem.org/downloads/) | **Mandatory** — powers the virtual controllers. The app detects it automatically on startup and guides you through installation if missing |
| HidHide driver *(optional)* | Hides your physical controller so games only see the virtual one |

---

## 🚫 Redistribution Notice

© 2026 **Blank RL** — All rights reserved.

This program is provided as-is for personal use. Unauthorized copying, modification, reverse engineering or redistribution of this software is prohibited.

---
<p align="center"><b>Blank RL</b></p>
