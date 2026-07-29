# 🎧 EasyHFP - Windows Bluetooth Audio Manager

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https.img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Language](https://img.shields.io/badge/Language-C%23%20%7C%20PowerShell-purple.svg)]()

**EasyHFP** is a lightweight Windows utility (available as a standalone C# executable and a PowerShell script) designed to solve the infamous **Bluetooth Audio Quality & Profile Freeze** problem on Windows 10 and 11.

---

## 🛑 The Problem

When you join a voice call (Discord, Zoom, Teams, Google Meet, etc.) while listening to music or watching a video, Windows automatically switches your Bluetooth headset to the **Hands-Free Telephony (HFP)** profile. 

This causes two major issues:
1. **Muffled / Low Quality Audio**: Media audio quality drops drastically (mono 8kHz/16kHz bandwidth shared with the microphone).
2. **Audio Freeze / Dropouts**: When trying to manually toggle HFP off in Device Manager or Windows settings, the Windows Audio Stack (`Audiosrv` / MMDeviceAPI) often freezes or gets stuck in SCO call mode, requiring a full Windows restart or manual Bluetooth toggle.

---

## ✨ The Solution: How EasyHFP Works

EasyHFP provides a **one-click or hotkey-driven toggle** to switch your Bluetooth headset between **High Quality A2DP Stereo Mode** and **HFP Microphone/Call Mode** smoothly without freezing your audio stack.

### 🔬 Technical Architecture
* **Direct Win32 Service State Management**: Uses `bthprops.cpl` P/Invoke (`BluetoothSetServiceState`) to directly enable or disable the Bluetooth Hands-Free Telephony service class GUID (`0000111e-0000-1000-8000-00805f9b34fb`).
* **Asymmetric Reconnect Strategy**:
  * **Enabling HFP (Call Mode)**: Performs a targeted PnP device node reset (`BTHENUM\DEV_<MAC>`) with a 3.5-second buffer to re-negotiate SCO call handles.
  * **Disabling HFP (Stereo Mode)**: Executes a graceful WinRT Master Bluetooth Radio Soft Toggle (`Windows.Devices.Radios.Radio.SetStateAsync`)—exactly matching the Windows Action Center Bluetooth button—enabling the headset to cleanly release SCO channels and negotiate a fresh A2DP Stereo link.
* **Config-Based Device Targeting**: Uses a local `config.txt` file to store your headset's MAC address and friendly name, eliminating the need to hardcode device names into scripts.
* **Rich Visual Feedback**: Features timestamped colored console logs and native Windows Toast & System Tray Balloon Notifications.

---

## ⚡ Features

- 🎯 **One-Time Interactive Setup**: Scans and lists all paired Bluetooth headsets on first run. Select your headset once and you're good to go.
- 🤫 **Silent Hotkey Operation**: Runs quietly in the background when invoked via hotkey or desktop shortcut.
- 🔄 **Reset & Switch Devices**: Run with `--setup` or `-s` anytime to re-configure for a new headset.
- 🔔 **Windows Toast Notifications**: Displays instant status popups indicating whether HFP is **ENABLED 🎙️** or **DISABLED 🎧**.
- 🛠️ **Zero Third-Party Dependencies**: Built entirely using native Windows APIs (Win32 P/Invoke & WinRT).

---

## 🚀 Quick Start Guide

### Option 1: Using Compiled `EasyHFP.exe` (Recommended)

1. Download or clone this repository.
2. Double-click `EasyHFP.exe`.
3. If running for the first time, an interactive console window will list your paired Bluetooth headsets. Enter the number corresponding to your headset (e.g. `1`).
4. `EasyHFP.exe` saves your choice to `config.txt`.
5. On subsequent runs, double-clicking `EasyHFP.exe` (or pressing a hotkey) will instantly toggle Hands-Free mode.

### Option 2: Using PowerShell (`Toggle-EasyHFP.ps1`)

Run the script in PowerShell (Administrator recommended for PnP reconnects):

```powershell
powershell -ExecutionPolicy Bypass -File .\Toggle-EasyHFP.ps1
```

---

## ⌨️ Assigning a Keyboard Shortcut (UAC-Free Hotkey Setup)

Since PnP driver reset requires elevated privileges, running the tool via a standard shortcut can trigger a UAC prompt. The recommended, built-in Windows method to bypass UAC prompts is using **Windows Task Scheduler**:

### Step 1: Create a Scheduled Task
1. Press `Win + R`, type `taskschd.msc` and hit **Enter**.
2. Click **Create Task...** on the right panel.
3. Under the **General** tab:
   * Name: `EasyHFP_Toggle`
   * Check **"Run with highest privileges"**.
4. Under the **Actions** tab:
   * Click **New...**
   * Action: **Start a program**
   * Program/script: `C:\Path\To\EasyHFP.exe`
   * Click **OK**.

### Step 2: Create Desktop Shortcut & Hotkey
1. Right-click on your Desktop -> **New** -> **Shortcut**.
2. Set location to:
   ```cmd
   schtasks /run /tn "EasyHFP_Toggle"
   ```
3. Name the shortcut `EasyHFP Toggle`.
4. Right-click the newly created desktop shortcut -> **Properties**.
5. Click on the **Shortcut key** field and press your preferred hotkey combination (e.g., `Ctrl + Alt + H`).
6. Click **OK**.

Now pressing `Ctrl + Alt + H` will instantly toggle your headset's Hands-Free mode without any UAC prompts!

---

## ⚠️ Disclaimer & Warning

> [!WARNING]
> During the **HFP -> A2DP (Disabling Hands-Free)** transition, EasyHFP briefly cycles the Windows Master Bluetooth Radio (soft toggle for ~3 seconds). 
> 
> * **Active Audio Streams**: If you are currently playing audio in a game, media player, or live streaming software (e.g., OBS Studio, DAW applications), the brief Bluetooth radio cycle may cause a temporary 2-3 second audio pause while your headset reconnects in high-quality A2DP stereo mode.
> * **Other Bluetooth Devices**: Other connected Bluetooth devices (mice, keyboards) may experience a brief 2-3 second disconnect while the radio toggles.

---

## 🛠️ Building from Source

### Compiling `Program.cs` via .NET Framework `csc.exe`

EasyHFP can be compiled on any Windows machine using the built-in .NET Framework C# compiler:

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:exe /out:EasyHFP.exe /r:"C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll Program.cs
```

---

## 📄 License

Distributed under the MIT License. See [`LICENSE`](LICENSE) for more information.

---

<p align="center">Made with ❤️ for a better Windows Bluetooth audio experience.</p>
