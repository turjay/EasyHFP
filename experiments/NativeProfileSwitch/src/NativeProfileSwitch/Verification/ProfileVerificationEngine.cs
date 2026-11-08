using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NativeProfileSwitch.Discovery;
using NativeProfileSwitch.Interop;
using NativeProfileSwitch.Models;
using static NativeProfileSwitch.Interop.CoreAudioInterop;

namespace NativeProfileSwitch.Verification
{
    public sealed class EndpointNotificationCollector : IMMNotificationClient, IDisposable
    {
        private readonly object _lock = new();
        private readonly List<EndpointNotificationRecord> _events = new();
        private readonly HashSet<string> _watchedEndpointIds;
        private readonly AutoResetEvent _signal = new(false);
        private IMMDeviceEnumerator? _enumerator;
        private bool _registered;

        public EndpointNotificationCollector(IEnumerable<string>? watchedEndpointIds = null)
        {
            _watchedEndpointIds = new HashSet<string>(
                watchedEndpointIds ?? Enumerable.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            try
            {
                var comType = typeof(MMDeviceEnumeratorComObject);
                _enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(comType)!;
                int hr = _enumerator.RegisterEndpointNotificationCallback(this);
                _registered = (hr == S_OK);
            }
            catch
            {
                _registered = false;
                _enumerator = null;
            }
        }

        public bool WaitForEvent(int timeoutMs) => _signal.WaitOne(timeoutMs);

        public List<EndpointNotificationRecord> GetEventsSnapshot()
        {
            lock (_lock)
            {
                return _events.ToList();
            }
        }

        public void ClearEvents()
        {
            lock (_lock)
            {
                _events.Clear();
                _signal.Reset();
            }
        }

        private bool ShouldRecord(string endpointId)
        {
            return _watchedEndpointIds.Count == 0 || _watchedEndpointIds.Contains(endpointId);
        }

        private void AddRecord(string type, string endpointId, string details)
        {
            if (!ShouldRecord(endpointId)) return;

            lock (_lock)
            {
                _events.Add(new EndpointNotificationRecord
                {
                    TimestampUtc = DateTime.UtcNow,
                    EventType = type,
                    EndpointId = endpointId,
                    Details = details
                });
                _signal.Set();
            }
        }

        public void OnDeviceStateChanged(string pwstrDeviceId, uint dwNewState)
        {
            AddRecord("OnDeviceStateChanged", pwstrDeviceId, $"NewState={FormatEndpointState(dwNewState)}");
        }

        public void OnDeviceAdded(string pwstrDeviceId)
        {
            AddRecord("OnDeviceAdded", pwstrDeviceId, "Endpoint added");
        }

        public void OnDeviceRemoved(string pwstrDeviceId)
        {
            AddRecord("OnDeviceRemoved", pwstrDeviceId, "Endpoint removed");
        }

        public void OnDefaultDeviceChanged(EDataFlow flow, ERole role, string pwstrDefaultDeviceId)
        {
            AddRecord("OnDefaultDeviceChanged", pwstrDefaultDeviceId ?? string.Empty, $"Flow={flow}, Role={role}");
        }

        public void OnPropertyValueChanged(string pwstrDeviceId, PROPERTYKEY key)
        {
            string keyName = FormatPropertyKey(key);
            AddRecord("OnPropertyValueChanged", pwstrDeviceId, $"Property={keyName}");
        }

        private static string FormatPropertyKey(PROPERTYKEY key)
        {
            if (key.fmtid == PKEY_AudioEngine_DeviceFormat.fmtid && key.pid == PKEY_AudioEngine_DeviceFormat.pid)
                return "PKEY_AudioEngine_DeviceFormat";
            if (key.fmtid == PKEY_AudioEngine_OEMFormat.fmtid && key.pid == PKEY_AudioEngine_OEMFormat.pid)
                return "PKEY_AudioEngine_OEMFormat";
            if (key.fmtid == PKEY_AudioEndpoint_PhysicalSpeakers.fmtid && key.pid == PKEY_AudioEndpoint_PhysicalSpeakers.pid)
                return "PKEY_AudioEndpoint_PhysicalSpeakers";
            return key.ToString();
        }

        public void Dispose()
        {
            if (_registered && _enumerator != null)
            {
                try { _enumerator.UnregisterEndpointNotificationCallback(this); } catch { }
                _registered = false;
            }

            if (_enumerator != null)
            {
                try { Marshal.ReleaseComObject(_enumerator); } catch { }
                _enumerator = null;
            }

            _signal.Dispose();
        }
    }

    public sealed class ProfileVerificationEngine
    {
        /// <summary>
        /// Captures a multi-signal verification snapshot for a headset's associated Render and Capture endpoints,
        /// distinguishing proxy observations from hardware packet/transport evidence.
        /// </summary>
        public ProfileVerificationSnapshot CaptureSnapshot(
            string phaseLabel,
            string? renderEndpointId,
            string? captureEndpointId,
            IReadOnlyList<EndpointNotificationRecord>? recentNotifications = null,
            long capturedFramesDeliveredByHardware = 0,
            long renderedFramesSubmitted = 0,
            bool communicationsRenderStreamActive = false)
        {
            var snapshot = new ProfileVerificationSnapshot
            {
                TimestampUtc = DateTime.UtcNow,
                PhaseLabel = phaseLabel,
                RenderEndpointId = renderEndpointId,
                CaptureEndpointId = captureEndpointId
            };

            if (recentNotifications != null)
            {
                snapshot.ObservedPropertyChangeEventsCount = recentNotifications.Count(e => e.EventType == "OnPropertyValueChanged");
                snapshot.ObservedStateChangeEventsCount = recentNotifications.Count(e => e.EventType == "OnDeviceStateChanged");
            }

            AudioEndpointInfo? renderInfo = null;
            AudioEndpointInfo? captureInfo = null;

            var comType = typeof(MMDeviceEnumeratorComObject);
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(comType)!;

            try
            {
                if (!string.IsNullOrEmpty(renderEndpointId) &&
                    enumerator.GetDevice(renderEndpointId, out var renderDev) == S_OK &&
                    renderDev != null)
                {
                    try
                    {
                        renderInfo = EndpointDiscoveryService.InspectDeviceReadOnly(renderDev, null, queryMixFormatCapabilities: true);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(renderDev);
                    }
                }

                if (!string.IsNullOrEmpty(captureEndpointId) &&
                    enumerator.GetDevice(captureEndpointId, out var captureDev) == S_OK &&
                    captureDev != null)
                {
                    try
                    {
                        captureInfo = EndpointDiscoveryService.InspectDeviceReadOnly(captureDev, null, queryMixFormatCapabilities: true);
                    }
                    finally
                    {
                        Marshal.ReleaseComObject(captureDev);
                    }
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            if (renderInfo != null)
            {
                snapshot.RenderStateText = renderInfo.StateText;
                snapshot.RenderDeviceFormat = renderInfo.PropertyStoreDeviceFormat;
                snapshot.RenderDefaultMixFormat = renderInfo.DefaultMixFormat;
                snapshot.RenderCommunicationsMixFormat = renderInfo.CommunicationsMixFormat;
                snapshot.SelfActiveRenderSessions = renderInfo.SelfActiveSessionCount;
                snapshot.ExternalActiveRenderSessions = renderInfo.ExternalActiveSessionCount;
            }

            if (captureInfo != null)
            {
                snapshot.CaptureStateText = captureInfo.StateText;
                snapshot.CaptureDeviceFormat = captureInfo.PropertyStoreDeviceFormat ?? captureInfo.DefaultMixFormat;
                snapshot.SelfActiveCaptureSessions = captureInfo.SelfActiveSessionCount;
                snapshot.ExternalActiveCaptureSessions = captureInfo.ExternalActiveSessionCount;
                snapshot.ExternalCaptureProcessIds = captureInfo.ExternalActiveProcessIds.ToList();
            }

            EvaluateEvidence(
                snapshot,
                renderInfo,
                captureInfo,
                capturedFramesDeliveredByHardware,
                renderedFramesSubmitted,
                communicationsRenderStreamActive);

            return snapshot;
        }

        public static void EvaluateEvidence(
            ProfileVerificationSnapshot snapshot,
            AudioEndpointInfo? renderInfo,
            AudioEndpointInfo? captureInfo,
            long capturedFramesDeliveredByHardware,
            long renderedFramesSubmitted,
            bool communicationsRenderStreamActive)
        {
            snapshot.EvidenceSummary.Clear();

            if (renderInfo != null && renderInfo.IsBluetoothLeAudio)
            {
                snapshot.InferredProfile = InferredTransportProfile.LeAudioUnapplicable;
                snapshot.Confidence = VerificationConfidence.High_MultiSignalConfirmed;
                snapshot.EvidenceSummary.Add("Endpoint uses Bluetooth LE Audio (TMAP/LC3), not Bluetooth Classic A2DP/HFP.");
                return;
            }

            bool renderActive = renderInfo?.RawState == DEVICE_STATE_ACTIVE;
            bool captureActive = captureInfo?.RawState == DEVICE_STATE_ACTIVE;

            if (!renderActive && !captureActive)
            {
                snapshot.InferredProfile = InferredTransportProfile.DisconnectedOrUnavailable;
                snapshot.Confidence = VerificationConfidence.High_MultiSignalConfirmed;
                snapshot.EvidenceSummary.Add($"Endpoints are not active (Render={snapshot.RenderStateText}, Capture={snapshot.CaptureStateText}).");
                return;
            }

            int totalActiveCaptureSessions = snapshot.SelfActiveCaptureSessions + snapshot.ExternalActiveCaptureSessions;

            if (snapshot.ExternalActiveCaptureSessions > 0)
            {
                snapshot.EvidenceSummary.Add(
                    $"[Session Evidence] External process(es) [{string.Join(", ", snapshot.ExternalCaptureProcessIds)}] hold {snapshot.ExternalActiveCaptureSessions} active capture session(s) on the headset microphone.");
            }

            if (snapshot.SelfActiveCaptureSessions > 0)
            {
                snapshot.EvidenceSummary.Add(
                    $"[Session Evidence] Current process holds {snapshot.SelfActiveCaptureSessions} active capture session(s) on the headset microphone.");
            }

            if (capturedFramesDeliveredByHardware > 0)
            {
                snapshot.EvidenceSummary.Add(
                    $"[Hardware Stream Evidence] BTHHFENUM capture driver actively delivered {capturedFramesDeliveredByHardware} audio frames via IAudioCaptureClient (requires active HFP SCO/eSCO capture link).");
            }

            if (communicationsRenderStreamActive && renderedFramesSubmitted > 0)
            {
                snapshot.EvidenceSummary.Add(
                    $"[Stream Proxy] Active AudioCategory_Communications render stream submitted {renderedFramesSubmitted} silent frames.");
            }

            if (snapshot.RenderDefaultMixFormat != null)
            {
                snapshot.EvidenceSummary.Add(
                    $"[Format Capability] Render DefaultMixFormat: {snapshot.RenderDefaultMixFormat.Summary}.");
            }

            if (snapshot.RenderCommunicationsMixFormat != null)
            {
                snapshot.EvidenceSummary.Add(
                    $"[Format Capability] Render CommunicationsMixFormat: {snapshot.RenderCommunicationsMixFormat.Summary}.");
            }

            if (snapshot.ObservedPropertyChangeEventsCount > 0 || snapshot.ObservedStateChangeEventsCount > 0)
            {
                snapshot.EvidenceSummary.Add(
                    $"[Notification Evidence] Observed {snapshot.ObservedPropertyChangeEventsCount} property-change and {snapshot.ObservedStateChangeEventsCount} state-change Core Audio events.");
            }

            // Determine inferred transport profile and confidence
            if (totalActiveCaptureSessions > 0 && capturedFramesDeliveredByHardware > 0)
            {
                snapshot.InferredProfile = InferredTransportProfile.HfpHandsFree;
                snapshot.Confidence = VerificationConfidence.High_MultiSignalConfirmed;
            }
            else if (totalActiveCaptureSessions > 0)
            {
                snapshot.InferredProfile = InferredTransportProfile.HfpHandsFree;
                snapshot.Confidence = VerificationConfidence.Medium_SessionAndFormatProxy;
            }
            else if (communicationsRenderStreamActive && renderedFramesSubmitted > 0)
            {
                // A running Communications render stream on Windows 11 unified endpoints requests voice routing,
                // but without capture packet flow or transport-level confirmation it is a proxy signal unless corroborated by endpoint property notifications.
                bool hasMonoRenderFormatOrNotifications =
                    (snapshot.RenderDeviceFormat?.Channels == 1) ||
                    (snapshot.ObservedPropertyChangeEventsCount > 0);

                snapshot.InferredProfile = InferredTransportProfile.HfpHandsFree;
                snapshot.Confidence = hasMonoRenderFormatOrNotifications
                    ? VerificationConfidence.Medium_SessionAndFormatProxy
                    : VerificationConfidence.Low_ApiReturnOnly;
                snapshot.EvidenceSummary.Add(
                    "Note: Communications render stream alone without capture frames is an OS routing signal; verify audibly or via endpoint format notification.");
            }
            else if (renderActive && totalActiveCaptureSessions == 0)
            {
                snapshot.InferredProfile = InferredTransportProfile.A2dpStereo;
                snapshot.Confidence = (snapshot.RenderDefaultMixFormat?.Channels >= 2)
                    ? VerificationConfidence.High_MultiSignalConfirmed
                    : VerificationConfidence.Medium_SessionAndFormatProxy;
                snapshot.EvidenceSummary.Add(
                    "[Idle Capture Evidence] Zero active capture sessions on headset microphone; Windows 11 routes playback via A2DP stereo.");
            }
            else
            {
                snapshot.InferredProfile = InferredTransportProfile.Unknown;
                snapshot.Confidence = VerificationConfidence.None;
            }
        }
    }
}
