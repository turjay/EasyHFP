<p align="center">
  <img src="easyhfp_logo.png" alt="EasyHFP Logo" width="160" style="border-radius: 12px;" />
</p>

# EasyHFP

**One click. Two audio modes. No Bluetooth restart.**

[![Windows 11](https://img.shields.io/badge/Platform-Windows%2011%20x64-0078D6.svg)](https://www.microsoft.com/windows)
[![.NET 8](https://img.shields.io/badge/.NET-8.0-512BD4.svg)](https://dotnet.microsoft.com/)
[![Release](https://img.shields.io/badge/Release-v0.1.0--beta.1-orange.svg)](https://github.com/turjay/EasyHFP/releases/tag/v0.1.0-beta.1)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)
[![Build & Test](https://github.com/turjay/EasyHFP/actions/workflows/build-and-test.yml/badge.svg)](https://github.com/turjay/EasyHFP/actions/workflows/build-and-test.yml)

EasyHFP is a lightweight Windows 11 utility for switching Bluetooth Classic headphones between high-quality stereo audio (**A2DP**) and hands-free microphone mode (**HFP**) without restarting the Bluetooth adapter or disconnecting your headset.

## Why EasyHFP?

Bluetooth Classic headphones use two separate modes: **A2DP** for high-quality stereo listening, and **HFP** for two-way voice audio when the headset microphone is active. Depending on Windows audio routing, drivers, and background apps, switching between these modes or returning to stereo audio can be inconvenient or inconsistent.

EasyHFP provides a simple one-click control and global shortcut to request the mode you want.

## Features

- One-click switching between **Stereo (A2DP)** and **Hands-Free (HFP)**
- No Bluetooth adapter restart, PnP device reset, or headset disconnect during normal switching
- Guided first-run setup for headset selection and Windows integration
- Windows Start search integration (per-user Start Menu shortcut, enabled by default)
- Optional Start with Windows (`--tray` background mode, disabled by default)
- Lightweight Windows system tray utility with global shortcut (`Ctrl+Alt+H`)
- Portable self-contained Windows x64 executable (no separate .NET Runtime required)
- Uses native Windows Core Audio APIs (`MMDEVAPI` / `WASAPI`) without administrator privileges
- Privacy-preserving: immediately discards microphone buffers in memory and never records, stores, or transmits audio

## Download (Public Beta)

Download **`EasyHFP.exe`** from the [**GitHub Releases (`v0.1.0-beta.1`)**](https://github.com/turjay/EasyHFP/releases/tag/v0.1.0-beta.1) page:

1. Download `EasyHFP.exe` (self-contained portable Windows 11 x64 executable — no .NET Runtime installation needed).
2. Place `EasyHFP.exe` in a permanent folder (for example, `%LOCALAPPDATA%\Programs\EasyHFP\EasyHFP.exe`).
3. Run `EasyHFP.exe`. *(Note: The beta executable is not code-signed; if Windows SmartScreen prompts on first launch, click **More info → Run anyway**.)*

*(This Git repository is source-only and does not track compiled binaries in commits.)*

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
- **Microphone permissions & privacy:** Because HFP is held active via a shared-mode WASAPI capture stream on the headset microphone, Windows *Privacy & security > Microphone* must allow desktop apps to access the microphone. The Windows taskbar microphone indicator is shown normally while HFP is held. EasyHFP immediately releases every capture buffer without reading, recording, saving, or transmitting audio samples.

## How It Works

On Windows 11, Bluetooth Classic headsets expose a unified audio endpoint model where Windows automatically switches the headset into HFP when its microphone capture endpoint is opened, and returns to A2DP when no capture streams remain active.

EasyHFP works with this native behavior:

- Uses Windows Core Audio (`MMDEVAPI` and `WASAPI`) interfaces to map your headset's playback and capture endpoints.
- Starts and holds a shared-mode WASAPI capture session on the headset microphone when HFP is requested.
- Immediately discards all captured audio buffers in memory without reading, recording, or storing samples.
- Stops and releases the capture session when A2DP is requested, allowing Windows to return the headset to stereo playback.

## Compatibility & Known Limitations

- **Windows 11 (x64):** Primary target. Tested on Windows 11 Pro 25H2 (Build 26200) with Marshall MONITOR III A.N.C.
- **Windows 10:** Not verified with the unified-endpoint WASAPI switching mechanism, as Windows 10 exposes separate Stereo and Hands-Free AG playback endpoints.
- **Bluetooth Classic only:** Designed for Bluetooth Classic headsets using A2DP and HFP. Bluetooth LE Audio (`TMAP` / `LC3`) uses a different architecture and is not supported.
- **External microphone applications:** If another application (such as Teams, Zoom, Discord, or Windows Sound Settings) is actively using the headset microphone, Windows will keep the headset in HFP even after EasyHFP releases its own stream.
- **Codec transition delay:** Core Audio session state reflects software stream state rather than physical Bluetooth link telemetry; headset firmware may take a brief moment after capture release before stereo A2DP audio becomes audible again.

## Build from Source

Requires the [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) on Windows:

```powershell
git clone https://github.com/turjay/EasyHFP.git
cd EasyHFP
dotnet build EasyHFP.sln -c Release
dotnet test EasyHFP.sln -c Release
```

To publish a portable self-contained single-file Windows x64 executable:

```powershell
dotnet publish src/EasyHFP.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishTrimmed=false
```

- Framework-dependent build output: `src/bin/Release/net8.0-windows10.0.19041.0/EasyHFP.exe`
- Self-contained single-file publish output: `src/bin/Release/net8.0-windows10.0.19041.0/win-x64/publish/EasyHFP.exe`

## Contributing

Issues, hardware compatibility reports, bug reports, and pull requests are welcome. Please open an issue on GitHub if you encounter headset-specific behavior or edge cases.

## License

Released under the [MIT License](LICENSE).
