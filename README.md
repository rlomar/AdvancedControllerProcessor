# Advanced Controller Processor

<p align="center">
  <a href="https://github.com/rlomar/AdvancedControllerProcessor/releases/latest/download/AdvancedControllerProcessor.exe">
    <img src="https://img.shields.io/badge/%E2%AC%87_DOWNLOAD_LATEST-v1.6.1-8B5CF6?style=for-the-badge&labelColor=171C28" alt="Download Latest" />
  </a>
</p>

## ⬇️ Direct Download (one click)

**[▶ DOWNLOAD AdvancedControllerProcessor.exe](https://github.com/rlomar/AdvancedControllerProcessor/releases/latest/download/AdvancedControllerProcessor.exe)**

> Or browse all files on the [**Releases page**](../../releases) — the program file lives under **Assets** of each release, not in the repository file list.

---

**PS5 DualSense → Virtual Xbox 360 / DualShock 4 controller processor for Windows**

Transform your DualSense controller input with advanced processing — deadzones, response curves, speed multipliers, directional speeds and smoothing — then feed it into your games as a fully working virtual Xbox 360 or DualShock 4 controller.

> ⚠️ **Closed-source software.** This repository hosts ready-to-run releases only. No source code is published here.

> 🔒 **License required.** Every device needs an activation key issued by **Blank RL**. An internet connection is required to activate and to keep the program running. Contact Blank RL to get your key.

> 🔄 **Mandatory updates.** Builds older than the latest release show a blocking update screen at startup and cannot be used until updated (in-app one-click, or manual download). The floor can be raised any time via [`update-policy.json`](update-policy.json).

---

## ✨ Features

- 🎮 **DualSense support** — USB & Bluetooth
- 🕹️ **Per-stick processing** — Deadzone, Response Curves (Linear / Soft / Aggressive / Fast / Custom)
- ⚡ **Speed multipliers** — independent X/Y axis scaling
- 🧭 **Directional speed** — separate forward/backward/left/right tuning
- 🌊 **Input smoothing**
- 👀 **Live monitor** — real-time raw vs processed stick visualization
- 📊 **Latency monitor** — live pipeline & scheduling-wait stats on the dashboard
- 💾 **Profiles** — save / load / export / import
- ⌨️ **Hotkeys** — F8 toggles processing · F9 safe-mode reset
- 🎯 **Virtual output** — Xbox 360 (XInput) or DualShock 4

---

## 🔑 Activation (one time per PC)

1. Get your license key from **Blank RL**
2. Run the program — the activation window opens automatically
3. Type/paste your key (`XXXX-XXXX-XXXX-XXXX`) and click **Activate**

That's it — the key binds to your device automatically (1 device per key).
Changed your PC or reinstalled Windows? Contact Blank RL to reset the binding.

### Activation problems?

| Message | Meaning |
|---------|---------|
| *Key was not recognized* | Typo in the key — check every character |
| *Key has been revoked* | The key was disabled by Blank RL |
| *Already bound to another device* | The key is used on another PC — ask Blank RL to reset it |
| *Could not reach activation server* | Check your internet connection and retry |

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
| Internet connection | Required — license validation happens at startup and periodically |
| License key | Get yours from **Blank RL** — see [Activation](#-activation-one-time-per-pc) |
| [ViGEmBus driver](https://vigem.org/downloads/) | **Mandatory** — powers the virtual controllers. The app detects it automatically on startup and guides you through installation if missing |
| HidHide driver *(optional)* | Hides your physical controller so games only see the virtual one |

---

## 🚫 Redistribution Notice

© 2026 **Blank RL** — All rights reserved.

This program is provided as-is for personal use. Unauthorized copying, modification, reverse engineering or redistribution of this software is prohibited.

---
<p align="center"><b>Blank RL</b></p>
