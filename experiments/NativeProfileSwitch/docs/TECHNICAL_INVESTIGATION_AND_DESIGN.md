# EasyHFP — Technical Investigation, Experimental Validation & Production Architecture Design

## Official Microsoft References Consulted

1. **Windows Bluetooth Classic Audio Architecture (`BthA2dp.sys` & `BthHfAud.sys`)**  
   https://learn.microsoft.com/en-us/windows-hardware/drivers/bluetooth/bluetooth-classic-audio
2. **WASAPI `IAudioClient2` Interface (`Audioclient.h`)**  
   https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nn-audioclient-iaudioclient2
3. **`IAudioClient2::SetClientProperties` (`AudioClientProperties` & `AUDIO_STREAM_CATEGORY`)**  
   https://learn.microsoft.com/en-us/windows/win32/api/audioclient/nf-audioclient-iaudioclient2-setclientproperties
4. **Audio Format Capabilities for Communication Scenarios**  
   https://learn.microsoft.com/en-us/windows/win32/coreaudio/communications-audio-format-capabilities

---

## 1. Technical Investigation (Questions 1–10)

### 1. How does Windows 11 select A2DP or HFP for Bluetooth Classic devices?

- Starting in Windows 11 (21H2+, Build 22000+), Microsoft unified the user-facing audio endpoint model for Bluetooth Classic headsets (`BthA2dp.sys` for A2DP Sink `0000110B` and `BthHfAud.sys` for Hands-Free `0000111E`). Instead of exposing two separate playback devices ("Headphones (Stereo)" and "Headset (Hands-Free AG Audio)") in the Sound control panel as Windows 10 did, Windows 11 presents a **single unified Render endpoint** alongside the headset's **Capture endpoint** under the same physical `PKEY_Device_ContainerId`.
- By default, when only standard media/console render streams (`AudioCategory_Media`, `AudioCategory_Other`, `AudioCategory_Movie`, `AudioCategory_GameMedia`) are active and **no** capture stream is open on the headset's microphone endpoint, Windows routes playback over **A2DP**.
- Windows 11 automatically transitions the headset from **A2DP to HFP (SCO)** when an application opens and starts an active audio capture stream (`IAudioClient::Initialize` + `Start`) on the headset's **Capture** endpoint (`BTHHFENUM`).
- When all HFP-triggering streams on the headset microphone are stopped and released, Windows 11 tears down the SCO audio connection and returns playback to **A2DP**.

### 2. Can a normal desktop application trigger that selection through public Windows APIs?

- **Yes.** A standard non-elevated Win32/.NET desktop application can use public Windows Core Audio (MMDEVAPI + WASAPI) COM interfaces (`IMMDeviceEnumerator`, `IMMDevice`, `IMMEndpoint`, `IPropertyStore`, `IAudioClient`, `IAudioClient2`, `IAudioCaptureClient`).
- Neither administrator privileges nor `BluetoothSetServiceState` calls are required to open or close a shared-mode WASAPI stream.
- **Prerequisite:** Both `AudioSinkServiceGuid` (`0000110B`) and `HandsFreeServiceGuid` (`0000111E`) must remain enabled (`BLUETOOTH_SERVICE_ENABLE`) in Windows so that both the Render and Capture endpoints exist in `MMDEVAPI`.

### 3. How does this behavior differ on Windows 10?

- **Windows 10 (Builds < 22000):** Exposes two distinct Render endpoints (*Headphones (Stereo)* via `BTHENUM` and *Headset (Hands-Free AG Audio)* via `BTHHFENUM`) plus one Capture endpoint. Opening the HFP capture endpoint activates the HFP SCO link on the headset, which suspends A2DP playback in headset firmware, but Windows 10 does not automatically re-route applications playing to the *Headphones (Stereo)* endpoint onto the *Headset (Hands-Free AG Audio)* render endpoint.
- **Windows 11 (Builds >= 22000):** Exposes one unified Render endpoint and one Capture endpoint under the same `PKEY_Device_ContainerId`. Applications continue rendering to the same `IMMDevice` endpoint ID while the Windows Bluetooth audio stack switches the underlying transport between A2DP and HFP.

### 4. Can profile selection occur without Bluetooth device reconnection?

- **Yes.** In Bluetooth Classic, the ACL connection remains active continuously. Switching between A2DP and HFP only opens or closes the synchronous SCO/eSCO audio channel and suspends/resumes the AVDTP stream over the existing ACL link without disconnecting the headset or disrupting other Bluetooth peripherals.

### 5. Can a capture stream reliably hold HFP active?

- **Yes.** Opening and starting (`IAudioClient::Start`) a shared-mode WASAPI capture stream on a Bluetooth Classic headset's capture endpoint (`BTHHFENUM`) requires `BthHfAud.sys` to establish and hold an HFP SCO audio channel with the headset for as long as the stream remains active and buffers are drained.

### 6. Can a Communications-category render stream trigger HFP without acquiring microphone audio?

- `IAudioClient2::SetClientProperties` allows setting `AudioCategory_Communications` (`eCategory = 3`) before calling `GetMixFormat` / `Initialize` on a Render endpoint.
- However, on many Windows 11 builds and Bluetooth drivers, a render-only `AudioCategory_Communications` stream without an active capture stream on the Bluetooth microphone continues using A2DP (preserving stereo quality when the user uses a separate USB/array microphone for VoIP) and only switches to HFP when the Bluetooth headset's own Capture endpoint is opened.

### 7. What observable signals distinguish actual HFP operation from mere Bluetooth service availability?

1. **Capture Endpoint Active WASAPI Session (`IAudioSessionManager2`):** Enumerating sessions on the headset's Capture endpoint and verifying `AudioSessionState.AudioSessionStateActive` (separating self-owned sessions from external processes).
2. **Capture Packet Clock Progression (`IAudioCaptureClient::GetNextPacketSize`):** Confirming that `GetBuffer` delivers non-zero frame counts from `BthHfAud.sys` (which clocks capture packets from incoming Bluetooth SCO frames).
3. **`IMMNotificationClient` Events:** Observing `OnPropertyValueChanged` and `OnDeviceStateChanged` callbacks during stream transitions.

### 8. Under which conditions might Windows refuse or delay returning to A2DP?

1. **External application holding the Bluetooth microphone:** Another process (e.g., Teams, Zoom, Discord, Voice Recorder, or Windows Sound Settings) has an active `AudioSessionStateActive` capture session on the headset's capture endpoint.
2. **Unreleased COM references:** If `IAudioCaptureClient` or `IAudioClient` is not deterministically stopped, reset, and released (`Marshal.ReleaseComObject`).
3. **Audio Engine Hysteresis / Codec Negotiation:** After the last capture session closes, Windows and the headset firmware require a brief grace and codec negotiation period before stereo A2DP playback becomes audible again.

### 9. How should we handle other applications actively using the Bluetooth microphone?

- Query `IAudioSessionManager2` -> `IAudioSessionEnumerator` -> `IAudioSessionControl2::GetProcessId` on the headset's Capture endpoint.
- If `ExternalActiveSessionCount > 0` when the user requests switching to A2DP:
  - Stop and release EasyHFP's own hold stream immediately.
  - Never interfere with the external application's audio session.
  - Report clearly that another process is still using the headset microphone so Windows remains in HFP until that application closes the mic.

### 10. What restrictions apply to LE Audio devices?

- Bluetooth LE Audio (`BTHLEDevice`, TMAP / HAP / BAP profiles using LC3 over Isochronous Channels) does not use Bluetooth Classic A2DP (`0000110B`) or HFP (`0000111E`).

---

## 2. Comparison of Experiment A vs. Experiment B

| Dimension | Experiment A: WASAPI Shared-Mode Capture Hold (`WasapiCaptureExperiment`) | Experiment B: `AudioCategory_Communications` Render (`CommunicationsRenderExperiment`) |
|---|---|---|
| **Mechanism** | Opens headset's Capture endpoint (`BTHHFENUM`) via `IAudioClient::Initialize(AUDCLNT_SHAREMODE_SHARED)` + `Start()`, drains and immediately discards capture buffers in-place without storing audio. | Opens headset's Render endpoint via `IAudioClient2::SetClientProperties(AudioCategory_Communications)` + `Initialize()` + `Start()` with `AUDCLNT_BUFFERFLAGS_SILENT`. |
| **Reliability on Windows 11** | **High (Primary Mechanism).** Directly exercises the documented Windows 11 condition *"An application opens the headset's capture endpoint"*, holding HFP active for as long as the stream runs. | **Driver-Dependent (Secondary Probe).** Often remains on A2DP unless a capture stream is also opened on the headset. |
| **Audio Side Effects** | Zero audio is recorded or written by EasyHFP; all captured mic frames are discarded in-place. Note that software session release completes before physical headset A2DP audio becomes audible again. | Streams `AUDCLNT_BUFFERFLAGS_SILENT` (zero amplitude). |
| **Windows Microphone Privacy Indicator** | Displays the system tray microphone-in-use indicator while HFP mode is held active; requires Windows Microphone privacy permission for desktop apps. | Does not open a capture endpoint. |
