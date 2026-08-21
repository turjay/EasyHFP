<p align="center">
  <img src="easy_hfp.jpeg" alt="EasyHFP Logo" width="140" style="border-radius: 12px;" />
</p>

# EasyHFP

[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg)](https://microsoft.com/windows)
[![Language](https://img.shields.io/badge/Language-C%23-purple.svg)]()
[![DPI](https://img.shields.io/badge/DPI-Per--Monitor%20V2-success.svg)]()

EasyHFP is a native, lightweight Windows utility designed to eliminate Bluetooth audio quality degradation and audio stack lockups when switching between the **A2DP Stereo** and **Hands-Free Telephony (HFP)** profiles on Windows 10 and 11.

---

## Overview & Problem Statement

When voice-enabled applications (such as Discord, Microsoft Teams, Zoom, or Google Meet) capture the default communication input device on Windows, the operating system automatically forces the connected Bluetooth headset into the **Hands-Free Telephony (HFP)** profile.

This architecture introduces two primary failure modes:

1. **Bandwidth Degradation (Audio Quality Drop):**
   The Bluetooth link transitions from high-fidelity A2DP Stereo (typically 44.1 kHz / 48 kHz SBC/AAC) to synchronous SCO/eSCO channels (8 kHz / 16 kHz mono bandwidth shared bidirectionally with the microphone). Output audio becomes severely muffled.

2. **Audio Stack Freeze & SCO Channel Lockup:**
   When attempting to manually disable the Hands-Free Telephony service via Device Manager or the Control Panel, the Windows Audio Service (`Audiosrv` / MMDeviceAPI) frequently hangs in a deadlock state. The headset becomes unresponsive or fails to negotiate a clean A2DP link, often requiring a system restart or full Bluetooth adapter cycle.

---

## Technical Architecture

EasyHFP implements a multi-layered transition strategy to cleanly switch Bluetooth audio profiles without causing deadlocks in the Windows audio subsystem.

```
+-------------------------------------------------------------+
|                         EasyHFP                             |
|   (GUI Controller / System Tray Daemon / CLI Automation)   |
+------------------------------+------------------------------+
                               |
        +----------------------+----------------------+
        |                                             |
        v                                             v
[ Enabling HFP (Call Mode) ]            [ Disabling HFP (Stereo Mode) ]
        |                                             |
  1. Win32 P/Invoke                             1. Win32 P/Invoke
     BluetoothSetServiceState                      BluetoothSetServiceState
     (Enable HFP Service GUID)                     (Disable HFP Service GUID)
        |                                             |
  2. Native SetupAPI                            2. WinRT Radio Soft-Toggle
     Targeted PnP Node Reset                       Release SCO Audio Handles
     (BTHENUM\DEV_<MAC>*)                          (Radio.SetStateAsync)
        |                                             |
  3. Clean SCO Handle Acquisition               3. Clean A2DP Link Negotiation
```

* **Direct Win32 Service State Management:** Utilizes `bthprops.cpl` P/Invoke (`BluetoothSetServiceState`) to programmatically toggle the Bluetooth Hands-Free Telephony service GUID (`0000111e-0000-1000-8000-00805f9b34fb`).
* **Native SetupAPI Device Node Management:** Directly interfaces with `setupapi.dll` (`SetupDiGetClassDevs`, `SetupDiSetClassInstallParams`, `SetupDiCallClassInstaller`) to reset device nodes in milliseconds without spawning child processes.
* **WinRT Radio Soft-Toggle:** Emulates the native Windows Action Center Bluetooth toggle via `Windows.Devices.Radios.Radio.SetStateAsync` to cleanly flush active L2CAP audio channels and SCO handles.
* **Per-Monitor V2 High-DPI Rendering:** Fully DPI-aware application designed for sharp rendering on 1440p (2K) and 4K high-density displays.

---

## Key Features

* **Desktop Graphical Interface:** Modern dark-theme control panel for profile toggling, device selection, and configuration.
* **System Tray Daemon:** Low-footprint background process residing in the Windows notification area with dynamic status icons.
* **Global Keyboard Shortcut (`Ctrl + Alt + H`):** System-wide hotkey listener using Win32 `RegisterHotKey` for instant profile switching during games or fullscreen applications.
* **Multi-Device Support:** Store multiple headset profiles and switch targets on the fly via the GUI or CLI.
* **Zero Third-Party Dependencies:** Implemented entirely using native Win32, SetupAPI, and WinRT APIs.
* **Automation & Scripting Friendly:** Comprehensive CLI flags for Stream Deck, batch files, and third-party automation tools.

---

## Quick Start

1. Download the latest [`EasyHFP.exe`](EasyHFP.exe) release.
2. Double-click `EasyHFP.exe` to launch the control panel.
3. Select your headset from the dropdown and click **Switch Audio Profile** or press **`Ctrl + Alt + H`**.

---

## Command Line Interface (CLI)

EasyHFP provides a full command-line interface for headless automation, scripting, and macro decks.

### Usage Syntax

```cmd
EasyHFP.exe [ACTION] [OPTIONS]
```

### Actions

| Action | Alias | Description |
| :--- | :--- | :--- |
| *(None)* | | Launches the Graphical User Interface (Default). |
| `--tray` | `--daemon` | Starts the resident System Tray background service with the global hotkey active. |
| `--toggle` | `-t` | Toggles the Hands-Free Telephony profile state for the target headset. |
| `--on` | `--enable` | Forces Hands-Free Telephony ON (Microphone / Call Mode). Idempotent. |
| `--off` | `--disable` | Forces Hands-Free Telephony OFF (High-Quality A2DP Stereo). Idempotent. |
| `--status` | | Queries and displays current link status and active audio profile. |
| `--list` | `-l` | Lists all paired Bluetooth audio devices, MAC addresses, and HFP states. |
| `--select` | `-s` | Runs an interactive setup wizard to select the default headset. |
| `--help` | `-h`, `/?` | Displays command-line reference and examples. |

### Options

| Option | Description |
| :--- | :--- |
| `--device, -d <MAC\|Name>` | Targets a specific headset instead of the saved default configuration. |
| `--reconnect-mode, -m <mode>` | Reconnection strategy: `radio` (default), `pnp`, or `none`. |
| `--silent` | Suppresses exit delay in console outputs. |

### Examples

```cmd
# Toggle the default headset profile
EasyHFP.exe --toggle

# Force high-quality stereo mode
EasyHFP.exe --off

# Target a specific device by friendly name or MAC
EasyHFP.exe --device "AirPods" --off
EasyHFP.exe --device 24C406917EC0 --status

# List all paired Bluetooth devices
EasyHFP.exe --list

# Start in background system tray mode
EasyHFP.exe --tray
```

---

## Reconnection Strategies

EasyHFP supports three reconnection modes configured via the GUI, `config.txt`, or the `--reconnect-mode` CLI argument:

* **`radio` (Recommended & Default):** Cycles the master Bluetooth radio for approximately 3 seconds when disabling Hands-Free mode. This ensures the Windows audio engine completely releases locked SCO handles.
* **`pnp`:** Resets only the target headset's hardware device node (`BTHENUM\DEV_<MAC>*`) via Win32 SetupAPI. Prevents brief disconnects of other Bluetooth peripherals (e.g., mice, keyboards).
* **`none`:** Modifies the Win32 Bluetooth service state without issuing hardware reset commands.

---

## Project Structure

```
EasyHFP/
├── src/
│   ├── EasyHFP.csproj    # .NET C# project file
│   └── Program.cs        # Unified application source (GUI, Tray, CLI, Win32 APIs)
├── .gitignore            # Git ignore rules
├── config.txt            # Local configuration store
├── easy_hfp.jpeg         # Application logo asset
├── EasyHFP.exe           # Standalone executable binary
├── LICENSE               # MIT License
└── README.md             # Technical documentation
```

---

## Building from Source

EasyHFP can be compiled on any standard Windows 10/11 environment using the built-in .NET Framework compiler (`csc.exe`):

```cmd
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /target:winexe /out:EasyHFP.exe /r:"C:\Program Files (x86)\Windows Kits\10\UnionMetadata\10.0.26100.0\Windows.winmd" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.WindowsRuntime.dll" /r:"C:\Windows\Microsoft.NET\Framework64\v4.0.30319\System.Runtime.dll" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll src\Program.cs
```

Alternatively, build using the modern .NET SDK:

```cmd
dotnet build src/EasyHFP.csproj -c Release
```

---

## License

Distributed under the MIT License. See [`LICENSE`](LICENSE) for details.
