using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EasyHFP.Interop;
using EasyHFP.Models;
using Microsoft.Win32;

namespace EasyHFP.Services
{
    /// <summary>
    /// Production Windows Core Audio profile controller.
    /// Switches a Bluetooth Classic headset between A2DP and HFP on Windows 11 by holding or releasing
    /// a zero-retention shared-mode WASAPI capture stream on the headset's capture endpoint.
    /// Never calls BluetoothSetServiceState, SetupAPI PnP restarts, or Bluetooth radio restarts.
    /// </summary>
    public sealed class WindowsAudioProfileController : IAudioProfileController
    {
        private readonly ICoreAudioEndpointMapper _endpointMapper;
        private readonly Func<string, string, IWasapiCaptureHoldSession> _sessionFactory;
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private readonly object _sessionSync = new();
        private readonly bool _subscribedSystemEvents;

        private AudioEndpointWatcher? _endpointWatcher;
        private IWasapiCaptureHoldSession? _activeSession;
        private int _disposed;

        public event Action? ProfileStateChanged;

        public bool IsHoldingHfpStream
        {
            get
            {
                lock (_sessionSync)
                {
                    return _activeSession != null && _activeSession.IsRunning;
                }
            }
        }

        public string? ActiveHoldDeviceMac
        {
            get
            {
                lock (_sessionSync)
                {
                    return (_activeSession != null && _activeSession.IsRunning)
                        ? _activeSession.TargetMac
                        : null;
                }
            }
        }

        public WindowsAudioProfileController(
            ICoreAudioEndpointMapper? endpointMapper = null,
            Func<string, string, IWasapiCaptureHoldSession>? sessionFactory = null,
            bool subscribeSystemEvents = true)
        {
            _endpointMapper = endpointMapper ?? new CoreAudioEndpointMapper();
            _sessionFactory = sessionFactory ?? ((mac, endpointId) => new WasapiCaptureHoldSession(mac, endpointId));
            _subscribedSystemEvents = subscribeSystemEvents;

            if (_subscribedSystemEvents)
            {
                try
                {
                    _endpointWatcher = new AudioEndpointWatcher();
                    _endpointWatcher.EndpointChanged += OnCoreAudioEndpointChanged;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Could not initialize AudioEndpointWatcher: {ex.Message}");
                }

                try
                {
                    SystemEvents.PowerModeChanged += OnPowerModeChanged;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Could not subscribe to SystemEvents.PowerModeChanged: {ex.Message}");
                }
            }
        }

        public HeadsetProfileSnapshot QueryProfileSnapshot(BtDeviceItem device)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            var map = _endpointMapper.ResolveHeadsetEndpoints(device.Mac, device.Name);

            bool heldBySelfSession;
            lock (_sessionSync)
            {
                heldBySelfSession = _activeSession != null &&
                                    _activeSession.IsRunning &&
                                    string.Equals(_activeSession.TargetMac, map.BluetoothMac, StringComparison.OrdinalIgnoreCase);
            }

            // If the device disconnected in Bluetooth and both Core Audio endpoints are gone, clean up any lingering hold
            bool endpointsActive = map.IsRenderActive || map.IsCaptureActive;
            if (!device.Connected && !endpointsActive)
            {
                if (heldBySelfSession)
                {
                    ReleaseAllHolds();
                    heldBySelfSession = false;
                }

                var disconnectedSnap = new HeadsetProfileSnapshot
                {
                    BluetoothMac = map.BluetoothMac,
                    DeviceName = device.Name,
                    IsBluetoothConnected = false,
                    HfpServiceRegistrationState = device.HfpState,
                    ContainerId = map.ContainerId,
                    RenderEndpointId = map.RenderEndpointId,
                    CaptureEndpointId = map.CaptureEndpointId,
                    IsRenderEndpointActive = false,
                    IsCaptureEndpointActive = false,
                    IsHeldByEasyHfp = false,
                    SelfActiveCaptureSessions = 0,
                    ExternalActiveCaptureSessions = 0,
                    ExternalCaptureProcessIds = Array.Empty<uint>(),
                    InferredMode = AudioProfileMode.Disconnected,
                    ModeDisplayText = "Disconnected",
                    StatusDetail = "Headset is disconnected."
                };

                device.ActiveProfileMode = disconnectedSnap.InferredMode;
                return disconnectedSnap;
            }

            AudioProfileMode inferredMode;
            string modeDisplayText;
            string statusDetail;

            if (heldBySelfSession || map.SelfActiveCaptureSessionCount > 0)
            {
                inferredMode = AudioProfileMode.HfpHeldByEasyHfp;
                modeDisplayText = "Hands-Free (HFP — Active)";
                statusDetail = map.ExternalActiveCaptureSessionCount > 0
                    ? $"HFP active (held by EasyHFP + external PID {string.Join(", ", map.ExternalCaptureProcessIds)})."
                    : "HFP held active by EasyHFP microphone stream.";
            }
            else if (map.ExternalActiveCaptureSessionCount > 0)
            {
                inferredMode = AudioProfileMode.HfpHeldByExternalApp;
                modeDisplayText = "Hands-Free (HFP — In Use)";
                statusDetail = $"HFP active (headset mic in use by PID {string.Join(", ", map.ExternalCaptureProcessIds)}).";
            }
            else if (map.IsRenderActive)
            {
                inferredMode = AudioProfileMode.A2dp;
                modeDisplayText = !map.IsCaptureActive && device.HfpState == HfpServiceState.Disabled
                    ? "Stereo (A2DP — HFP Service Disabled)"
                    : "Stereo (A2DP)";
                statusDetail = !map.IsCaptureActive && device.HfpState == HfpServiceState.Disabled
                    ? "Hands-Free Bluetooth service is disabled in Windows."
                    : "High-quality stereo playback active (zero active mic sessions).";
            }
            else
            {
                inferredMode = AudioProfileMode.Unknown;
                modeDisplayText = "Unavailable";
                statusDetail = "No active audio endpoints found for this device.";
            }

            var snapshot = new HeadsetProfileSnapshot
            {
                BluetoothMac = map.BluetoothMac,
                DeviceName = device.Name,
                IsBluetoothConnected = device.Connected || endpointsActive,
                HfpServiceRegistrationState = device.HfpState,
                ContainerId = map.ContainerId,
                RenderEndpointId = map.RenderEndpointId,
                CaptureEndpointId = map.CaptureEndpointId,
                IsRenderEndpointActive = map.IsRenderActive,
                IsCaptureEndpointActive = map.IsCaptureActive,
                IsHeldByEasyHfp = heldBySelfSession || map.SelfActiveCaptureSessionCount > 0,
                SelfActiveCaptureSessions = map.SelfActiveCaptureSessionCount,
                ExternalActiveCaptureSessions = map.ExternalActiveCaptureSessionCount,
                ExternalCaptureProcessIds = map.ExternalCaptureProcessIds,
                InferredMode = inferredMode,
                ModeDisplayText = modeDisplayText,
                StatusDetail = statusDetail
            };

            device.ActiveProfileMode = snapshot.InferredMode;
            return snapshot;
        }

        public async Task<ProfileTransitionResult> ToggleProfileAsync(
            BtDeviceItem device,
            CancellationToken cancellationToken = default)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            var snapshot = QueryProfileSnapshot(device);
            if (snapshot.IsHeldByEasyHfp || snapshot.InferredMode == AudioProfileMode.HfpHeldByExternalApp)
            {
                return await SwitchToA2dpAsync(device, cancellationToken).ConfigureAwait(false);
            }

            return await SwitchToHfpAsync(device, cancellationToken).ConfigureAwait(false);
        }

        public async Task<ProfileTransitionResult> SwitchToHfpAsync(
            BtDeviceItem device,
            CancellationToken cancellationToken = default)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            var totalWatch = Stopwatch.StartNew();
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var map = _endpointMapper.ResolveHeadsetEndpoints(device.Mac, device.Name);

                if (!device.Connected && !map.IsRenderActive && !map.IsCaptureActive)
                {
                    return new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.DeviceDisconnected,
                        TargetMode = AudioProfileMode.HfpHeldByEasyHfp,
                        ObservedMode = AudioProfileMode.Disconnected,
                        Duration = totalWatch.Elapsed,
                        UserMessage = $"{device.Name} is disconnected.",
                        DiagnosticDetails = "Cannot open WASAPI capture stream because headset is disconnected."
                    };
                }

                if (string.IsNullOrEmpty(map.CaptureEndpointId) || !map.IsCaptureActive)
                {
                    string userMsg = device.HfpState == HfpServiceState.Disabled
                        ? "Headset mic unavailable (Hands-Free service disabled in Windows)."
                        : "Headset microphone endpoint is not active in Windows Sound.";

                    return new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.CaptureEndpointUnavailable,
                        TargetMode = AudioProfileMode.HfpHeldByEasyHfp,
                        ObservedMode = map.IsRenderActive ? AudioProfileMode.A2dp : AudioProfileMode.Unknown,
                        Duration = totalWatch.Elapsed,
                        UserMessage = userMsg,
                        DiagnosticDetails = $"CaptureEndpointId='{map.CaptureEndpointId}', IsCaptureActive={map.IsCaptureActive}, HfpServiceState={device.HfpState}"
                    };
                }

                IWasapiCaptureHoldSession? existingSession;
                lock (_sessionSync)
                {
                    existingSession = _activeSession;
                }

                if (existingSession != null &&
                    existingSession.IsRunning &&
                    string.Equals(existingSession.TargetMac, map.BluetoothMac, StringComparison.OrdinalIgnoreCase))
                {
                    return new ProfileTransitionResult
                    {
                        Success = true,
                        Outcome = ProfileTransitionOutcome.AlreadyInTargetProfile,
                        TargetMode = AudioProfileMode.HfpHeldByEasyHfp,
                        ObservedMode = AudioProfileMode.HfpHeldByEasyHfp,
                        Duration = totalWatch.Elapsed,
                        FramesDiscarded = existingSession.TotalFramesDiscarded,
                        UserMessage = "Already in Hands-Free (HFP) mode."
                    };
                }

                if (existingSession != null)
                {
                    await StopAndDisposeSessionInternalAsync(existingSession).ConfigureAwait(false);
                }

                var newSession = _sessionFactory(map.BluetoothMac, map.CaptureEndpointId);
                newSession.SessionInvalidated += OnActiveSessionInvalidated;

                var startResult = await newSession.StartAsync(cancellationToken).ConfigureAwait(false);
                if (!startResult.Started)
                {
                    newSession.SessionInvalidated -= OnActiveSessionInvalidated;
                    newSession.Dispose();

                    if (startResult.HResult == NativeCoreAudio.E_ACCESSDENIED)
                    {
                        return new ProfileTransitionResult
                        {
                            Success = false,
                            Outcome = ProfileTransitionOutcome.MicrophoneAccessDenied,
                            TargetMode = AudioProfileMode.HfpHeldByEasyHfp,
                            ObservedMode = map.IsRenderActive ? AudioProfileMode.A2dp : AudioProfileMode.Unknown,
                            Duration = totalWatch.Elapsed,
                            UserMessage = "Microphone access denied. Check Windows Microphone Privacy settings.",
                            DiagnosticDetails = startResult.ErrorMessage
                        };
                    }

                    return new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.TransitionFailed,
                        TargetMode = AudioProfileMode.HfpHeldByEasyHfp,
                        ObservedMode = map.IsRenderActive ? AudioProfileMode.A2dp : AudioProfileMode.Unknown,
                        Duration = totalWatch.Elapsed,
                        UserMessage = "Could not start Hands-Free audio stream.",
                        DiagnosticDetails = startResult.ErrorMessage
                    };
                }

                lock (_sessionSync)
                {
                    _activeSession = newSession;
                }

                // Verify active capture session in Core Audio
                var postMap = _endpointMapper.ResolveHeadsetEndpoints(device.Mac, device.Name);
                bool verified = newSession.IsRunning &&
                                (postMap.SelfActiveCaptureSessionCount > 0 || startResult.InitialFramesDiscarded > 0);

                if (!verified)
                {
                    await StopAndDisposeSessionInternalAsync(newSession).ConfigureAwait(false);
                    return new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.TransitionFailed,
                        TargetMode = AudioProfileMode.HfpHeldByEasyHfp,
                        ObservedMode = AudioProfileMode.Unknown,
                        Duration = totalWatch.Elapsed,
                        UserMessage = "Capture stream started, but HFP activation could not be verified.",
                        DiagnosticDetails = $"SelfSessions={postMap.SelfActiveCaptureSessionCount}, InitialFrames={startResult.InitialFramesDiscarded}"
                    };
                }

                device.ActiveProfileMode = AudioProfileMode.HfpHeldByEasyHfp;
                NotifyProfileStateChanged();

                return new ProfileTransitionResult
                {
                    Success = true,
                    Outcome = ProfileTransitionOutcome.SwitchedToHfp,
                    TargetMode = AudioProfileMode.HfpHeldByEasyHfp,
                    ObservedMode = AudioProfileMode.HfpHeldByEasyHfp,
                    Duration = totalWatch.Elapsed,
                    FramesDiscarded = newSession.TotalFramesDiscarded,
                    ExternalCaptureProcessIds = postMap.ExternalCaptureProcessIds,
                    UserMessage = $"Switched to Hands-Free (HFP) ({totalWatch.Elapsed.TotalMilliseconds:F0} ms).",
                    DiagnosticDetails = $"CaptureEndpoint={map.CaptureEndpointId}, FirstPacketMs={startResult.FirstPacketLatencyMs:F1}, InitialFrames={startResult.InitialFramesDiscarded}"
                };
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public async Task<ProfileTransitionResult> SwitchToA2dpAsync(
            BtDeviceItem device,
            CancellationToken cancellationToken = default)
        {
            if (device == null) throw new ArgumentNullException(nameof(device));

            var totalWatch = Stopwatch.StartNew();
            await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                IWasapiCaptureHoldSession? sessionToStop;
                lock (_sessionSync)
                {
                    sessionToStop = _activeSession;
                }

                bool hadEasyHfpHold = sessionToStop != null;
                long framesDiscardedBeforeStop = sessionToStop?.TotalFramesDiscarded ?? 0;

                if (sessionToStop != null)
                {
                    await StopAndDisposeSessionInternalAsync(sessionToStop).ConfigureAwait(false);
                }

                // Poll up to 1000ms for Core Audio session manager to reflect 0 self-owned active capture sessions
                HeadsetAudioEndpointMap postMap = _endpointMapper.ResolveHeadsetEndpoints(device.Mac, device.Name);
                var verifyWatch = Stopwatch.StartNew();
                while (postMap.SelfActiveCaptureSessionCount > 0 &&
                       verifyWatch.ElapsedMilliseconds < 1000 &&
                       !cancellationToken.IsCancellationRequested)
                {
                    await Task.Delay(50, cancellationToken).ConfigureAwait(false);
                    postMap = _endpointMapper.ResolveHeadsetEndpoints(device.Mac, device.Name);
                }

                // Requirement 10: Never claim A2DP restoration when an external process holds the headset microphone
                if (postMap.ExternalActiveCaptureSessionCount > 0)
                {
                    device.ActiveProfileMode = AudioProfileMode.HfpHeldByExternalApp;
                    NotifyProfileStateChanged();

                    string pidList = string.Join(", ", postMap.ExternalCaptureProcessIds);
                    if (hadEasyHfpHold)
                    {
                        return new ProfileTransitionResult
                        {
                            Success = false,
                            Outcome = ProfileTransitionOutcome.ReleasedEasyHfpHoldButExternalAppActive,
                            TargetMode = AudioProfileMode.A2dp,
                            ObservedMode = AudioProfileMode.HfpHeldByExternalApp,
                            Duration = totalWatch.Elapsed,
                            FramesDiscarded = framesDiscardedBeforeStop,
                            ExternalCaptureProcessIds = postMap.ExternalCaptureProcessIds,
                            UserMessage = $"Released EasyHFP hold; mic still in use by another app (PID {pidList}).",
                            DiagnosticDetails = $"SelfSessions={postMap.SelfActiveCaptureSessionCount}, ExternalSessions={postMap.ExternalActiveCaptureSessionCount} [{pidList}]"
                        };
                    }

                    return new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.CannotReturnToA2dpExternalAppActive,
                        TargetMode = AudioProfileMode.A2dp,
                        ObservedMode = AudioProfileMode.HfpHeldByExternalApp,
                        Duration = totalWatch.Elapsed,
                        ExternalCaptureProcessIds = postMap.ExternalCaptureProcessIds,
                        UserMessage = $"Cannot return to Stereo while another app (PID {pidList}) uses the mic.",
                        DiagnosticDetails = $"ExternalSessions={postMap.ExternalActiveCaptureSessionCount} [{pidList}]"
                    };
                }

                if (!device.Connected && !postMap.IsRenderActive && !postMap.IsCaptureActive)
                {
                    device.ActiveProfileMode = AudioProfileMode.Disconnected;
                    NotifyProfileStateChanged();

                    return new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.DeviceDisconnected,
                        TargetMode = AudioProfileMode.A2dp,
                        ObservedMode = AudioProfileMode.Disconnected,
                        Duration = totalWatch.Elapsed,
                        FramesDiscarded = framesDiscardedBeforeStop,
                        UserMessage = $"{device.Name} is disconnected."
                    };
                }

                if (postMap.SelfActiveCaptureSessionCount > 0)
                {
                    return new ProfileTransitionResult
                    {
                        Success = false,
                        Outcome = ProfileTransitionOutcome.TransitionFailed,
                        TargetMode = AudioProfileMode.A2dp,
                        ObservedMode = AudioProfileMode.HfpHeldByEasyHfp,
                        Duration = totalWatch.Elapsed,
                        FramesDiscarded = framesDiscardedBeforeStop,
                        UserMessage = "Failed to release capture session cleanly.",
                        DiagnosticDetails = $"SelfActiveCaptureSessionCount remained {postMap.SelfActiveCaptureSessionCount} after stop."
                    };
                }

                device.ActiveProfileMode = AudioProfileMode.A2dp;
                NotifyProfileStateChanged();

                return new ProfileTransitionResult
                {
                    Success = true,
                    Outcome = hadEasyHfpHold
                        ? ProfileTransitionOutcome.SwitchedToA2dp
                        : ProfileTransitionOutcome.AlreadyInTargetProfile,
                    TargetMode = AudioProfileMode.A2dp,
                    ObservedMode = AudioProfileMode.A2dp,
                    Duration = totalWatch.Elapsed,
                    FramesDiscarded = framesDiscardedBeforeStop,
                    UserMessage = hadEasyHfpHold
                        ? $"Switched to Stereo (A2DP) ({totalWatch.Elapsed.TotalMilliseconds:F0} ms)."
                        : "Already in Stereo (A2DP) mode.",
                    DiagnosticDetails = $"Released hold; SelfSessions=0, ExternalSessions=0, RenderActive={postMap.IsRenderActive}"
                };
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public void ReleaseAllHolds()
        {
            IWasapiCaptureHoldSession? session;
            lock (_sessionSync)
            {
                session = _activeSession;
                _activeSession = null;
            }

            if (session != null)
            {
                session.SessionInvalidated -= OnActiveSessionInvalidated;
                try
                {
                    session.Dispose();
                }
                catch (Exception ex)
                {
                    AppLogger.Warn($"Error disposing WASAPI hold session: {ex.Message}");
                }
                NotifyProfileStateChanged();
            }
        }

        private async Task StopAndDisposeSessionInternalAsync(IWasapiCaptureHoldSession session)
        {
            lock (_sessionSync)
            {
                if (ReferenceEquals(_activeSession, session))
                {
                    _activeSession = null;
                }
            }

            session.SessionInvalidated -= OnActiveSessionInvalidated;
            try
            {
                await session.StopAndReleaseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Error stopping WASAPI hold session: {ex.Message}");
            }
            finally
            {
                try { session.Dispose(); } catch { }
            }
        }

        private void OnActiveSessionInvalidated(string reason)
        {
            AppLogger.Info($"Active WASAPI HFP hold session invalidated ({reason}); cleaning up session.");
            ReleaseAllHolds();
        }

        private void OnCoreAudioEndpointChanged()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            NotifyProfileStateChanged();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (Volatile.Read(ref _disposed) != 0) return;

            if (e.Mode == PowerModes.Suspend)
            {
                AppLogger.Info("System entering Suspend; releasing active WASAPI HFP capture hold session.");
                ReleaseAllHolds();
            }
            else if (e.Mode == PowerModes.Resume)
            {
                AppLogger.Info("System resumed from Suspend; refreshing audio profile state.");
                NotifyProfileStateChanged();
            }
        }

        private void NotifyProfileStateChanged()
        {
            try
            {
                ProfileStateChanged?.Invoke();
            }
            catch { }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_subscribedSystemEvents)
            {
                try
                {
                    SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                }
                catch { }
            }

            if (_endpointWatcher != null)
            {
                try
                {
                    _endpointWatcher.EndpointChanged -= OnCoreAudioEndpointChanged;
                    _endpointWatcher.Dispose();
                }
                catch { }
                _endpointWatcher = null;
            }

            ReleaseAllHolds();
            _operationLock.Dispose();
        }
    }
}
