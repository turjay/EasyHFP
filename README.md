<p align="center">
  <img src="easy_hfp.jpeg" alt="EasyHFP Logo" width="128" style="border-radius: 12px;" />
</p>

# EasyHFP

**One click. Two audio modes. No Bluetooth restart.**

[![Windows 11](https://img.shields.io/badge/Platform-Windows%2011-0078D6.svg)](https://www.microsoft.com/windows)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Build & Test](https://github.com/turjay/EasyHFP/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/turjay/EasyHFP/actions/workflows/build-and-test.yml)

EasyHFP is a lightweight Windows utility for switching Bluetooth Classic headphones between high-quality stereo audio (A2DP) and hands-free audio (HFP) without restarting the Bluetooth adapter.

## Why EasyHFP?

Bluetooth Classic headphones use two separate modes: **A2DP** for high-quality stereo listening, and **HFP** for two-way voice audio when the headset microphone is active. Depending on Windows audio routing, drivers, and background apps, switching between these modes or returning to stereo audio can be inconvenient or inconsistent.

EasyHFP provides a simple one-click control and global shortcut to request the mode you want.

## Features

- One-click switching between A2DP and HFP
- No Bluetooth adapter restart during normal switching
- Guided first-run setup for headset selection and Windows integration
- Windows Start search integration (per-user Start Menu shortcut, enabled by default)
- Optional Start with Windows (`--tray` background mode, disabled by default)
- Lightweight Windows system tray utility with global shortcut (`Ctrl+Alt+H`)
- No administrator privileges required for normal WASAPI operation
- Uses native Windows Core Audio APIs
- Does not save, record, or transmit microphone audio

## Screenshot

The main window provides a compact device selector, connection and active mode status, a single switch button, and basic tray/startup/Start Menu preferences.

*(Documentation note: Capture the main utility window and save it to [`assets/screenshot.png`](assets/README.md) when preparing the next visual update.)*

## Download

This repository is **source-only** and does not track compiled `.exe` or `.dll` binaries in Git. Distributable builds are published separately on the [GitHub Releases](https://github.com/turjay/EasyHFP/releases) page, or you can build directly from source using the instructions below.

## First-Run Setup & Usage

1. Connect a compatible Bluetooth Classic headset to Windows.
2. Launch `EasyHFP.exe`.
3. On first launch, the **EasyHFP Setup** wizard guides you through three quick steps:
   - **Select Headset:** Pick your preferred Bluetooth headset and verify that its Stereo (A2DP) and Hands-Free (HFP) audio endpoints are detected.
   - **Preferences:** Choose whether to add EasyHFP to **Windows Start search** (enabled by default), **Launch EasyHFP when Windows starts** (disabled by default), close to the system tray, and show desktop notifications.
   - **Finish:** Save your settings to `%APPDATA%\EasyHFP\config.json` (existing legacy `config.txt` files are migrated automatically) and open the main window.
4. Click the main switch button to toggle between **Stereo (A2DP)** and **Hands-Free (HFP)**.
5. Optionally press **`Ctrl+Alt+H`** from anywhere, or toggle modes from the system tray icon.

### Important notes

- **Windows Start search vs. Start with Windows:** Adding EasyHFP to Windows Start search creates a per-user shortcut at `%APPDATA%\Microsoft\Windows\Start Menu\Programs\EasyHFP.lnk` so you can launch EasyHFP from the Start menu or Windows search bar. It is completely independent of *Start with Windows*, which registers `"<path>\EasyHFP.exe" --tray` under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`. If you move the portable `EasyHFP.exe` to a new folder, EasyHFP automatically updates enabled shortcut and startup paths on next launch.
- **System tray:** When *Close to tray* is enabled, minimizing or closing the window keeps EasyHFP running quietly in the notification area. Left-clicking the tray icon restores the window.
- **Bluetooth services:** Both **Audio Sink (A2DP)** and **Handsfree Telephony (HFP)** services must remain enabled for the headset in Windows so both playback and capture endpoints are available.
- **Microphone permissions:** Because HFP is held active via a capture stream on the headset microphone, Windows *Privacy & security > Microphone* must allow desktop apps to access the microphone. The Windows taskbar microphone indicator is shown normally while HFP is held.
- **External applications:** If another application (such as Teams, Zoom, Discord, or Windows Sound Settings) is actively using the headset microphone, Windows will keep the headset in HFP even after EasyHFP releases its own stream.

## How It Works

On Windows 11, Bluetooth Classic headsets expose a unified audio endpoint model where Windows automatically switches the headset into HFP when its microphone capture endpoint is opened, and returns to A2DP when no capture streams remain active.

EasyHFP works with this native behavior:

- Uses Windows Core Audio (`MMDEVAPI` and `WASAPI`) interfaces to map your headset's playback and capture endpoints.
- Starts and holds a shared-mode WASAPI capture session on the headset microphone when HFP is requested.
- Immediately discards all captured audio buffers in memory without reading, recording, or storing samples.
- Stops and releases the capture session when A2DP is requested, allowing Windows to return the headset to stereo playback.

Normal switching never toggles Bluetooth driver service registration (`BluetoothSetServiceState`), never restarts PnP device nodes, and never power-cycles the Bluetooth radio. Note that Core Audio session state reflects software stream state rather than direct physical Bluetooth link telemetry, and headset codec negotiation may take a brief moment after session release before stereo audio becomes audible again.

## Compatibility

- **Windows 11:** Primary target. Tested on Windows 11 Pro 25H2 (Build 26200) with Marshall MONITOR III A.N.C.
- **Windows 10:** Not verified with the unified-endpoint WASAPI switching mechanism, as Windows 10 exposes separate Stereo and Hands-Free AG playback endpoints.
- **Bluetooth Classic:** Designed for Bluetooth Classic headsets using A2DP and HFP.
- **Bluetooth LE Audio:** Uses a different transport architecture (`TMAP` / `LC3`) and is not supported by this implementation.

Actual switching behavior and codec transition speed may vary across headset firmware and Bluetooth drivers.

## Build from Source

Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows:

```powershell
git clone https://github.com/turjay/EasyHFP.git
cd EasyHFP
dotnet build EasyHFP.sln -c Release
```

To run the automated unit test suite:

```powershell
dotnet test EasyHFP.sln -c Release
```

The built executable is located at `src/bin/Release/net8.0-windows10.0.19041.0/EasyHFP.exe`.

## Contributing

Issues, hardware compatibility reports, bug reports, and pull requests are welcome. Please open an issue on GitHub if you encounter headset-specific behavior or edge cases.

## License

Released under the [MIT License](LICENSE).
