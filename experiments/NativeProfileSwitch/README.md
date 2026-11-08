# NativeProfileSwitch — Windows Core Audio A2DP/HFP Research & Diagnostic Harness

An isolated .NET 8 / Windows Core Audio (MMDEVAPI + WASAPI) experimental tool and test harness for investigating and validating native audio-stream-driven Bluetooth Classic profile switching (**A2DP** ↔ **HFP**) on Windows 11 without restarting the Bluetooth radio, restarting PnP device nodes, or toggling Bluetooth service registration.

## Project Layout

```text
experiments/NativeProfileSwitch/
├── NativeProfileSwitch.sln
├── README.md
├── docs/
│   └── TECHNICAL_INVESTIGATION_AND_DESIGN.md
├── src/
│   └── NativeProfileSwitch/
│       ├── NativeProfileSwitch.csproj
│       ├── Program.cs                         # CLI entry point with safety gate (--confirm)
│       ├── Interop/
│       │   └── CoreAudioInterop.cs            # MMDEVAPI & WASAPI COM interop declarations
│       ├── Models/
│       │   └── ExperimentModels.cs            # Endpoint, group, verification & report models
│       ├── Discovery/
│       │   └── EndpointDiscoveryService.cs    # Read-only endpoint enumeration & container grouping
│       ├── Verification/
│       │   └── ProfileVerificationEngine.cs   # Multi-signal A2DP/HFP verification engine
│       ├── Experiments/
│       │   ├── WasapiCaptureExperiment.cs     # Experiment A: Shared-mode WASAPI capture hold
│       │   ├── CommunicationsRenderExperiment.cs # Experiment B: IAudioClient2 Communications render
│       │   └── ExperimentHarness.cs           # Repeatable test harness (Tests A, B, C, D, E)
│       └── Diagnostics/
│           └── StructuredDiagnosticLogger.cs  # JSON & text report persistence
└── tests/
    └── NativeProfileSwitch.Tests/
        ├── NativeProfileSwitch.Tests.csproj
        └── ExperimentAndVerificationTests.cs  # Automated xUnit test suite
```

## Build & Run Automated Tests

From the `experiments/NativeProfileSwitch` directory:

```powershell
dotnet test NativeProfileSwitch.sln -c Release
```

## CLI Usage

### 1. Read-Only Endpoint Discovery & Mapping (Safe, No Audio Stream Changes)

Enumerates all Render and Capture endpoints across all states (`Active`, `Disabled`, `NotPresent`, `Unplugged`), groups them by `PKEY_Device_ContainerId` and extracted Bluetooth MAC address (`PKEY_AudioEndpoint_Association`), and saves a JSON discovery snapshot in `logs/`:

```powershell
dotnet run --project src/NativeProfileSwitch -c Release -- --enumerate
```

---

## Manual Bluetooth Hardware Test Plan (User-Initiated)

> **Prerequisite:**  
> Both the **Audio Sink** (`0000110b-0000-1000-8000-00805f9b34fb`) and **Hands-Free** (`0000111e-0000-1000-8000-00805f9b34fb`) Bluetooth services must be enabled in Windows so that both the `[RENDER]` and `[CAPTURE]` endpoints are `Active` in `MMDEVAPI` (`Win11 Unified Topo : True`).

All active stream experiments require the explicit `--confirm` flag so they cannot be triggered accidentally. Every run writes both a `.json` and `.txt` diagnostic log to `experiments/NativeProfileSwitch/logs/` (git-ignored).

### Test A — Capture-Triggered HFP & Return to A2DP (Experiment A)
```powershell
dotnet run --project src/NativeProfileSwitch -c Release -- --test-a --hold-seconds 6 --confirm
```

### Test B — Communications Render Stream Probe (Experiment B)
```powershell
dotnet run --project src/NativeProfileSwitch -c Release -- --test-b --hold-seconds 6 --confirm
```

### Test C — Repeated Cycling Stability (5 Cycles)
```powershell
dotnet run --project src/NativeProfileSwitch -c Release -- --test-c --cycles 5 --hold-seconds 4 --pause-seconds 3 --confirm
```

### Test D — External Microphone Contention Detection
```powershell
dotnet run --project src/NativeProfileSwitch -c Release -- --test-d --hold-seconds 4 --confirm
```

### Test E — Continuous Active Playback Transition Check
```powershell
dotnet run --project src/NativeProfileSwitch -c Release -- --test-e --hold-seconds 5 --confirm
```
