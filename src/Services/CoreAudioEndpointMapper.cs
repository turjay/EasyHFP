using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using EasyHFP.Interop;
using EasyHFP.Models;
using static EasyHFP.Interop.NativeCoreAudio;

namespace EasyHFP.Services
{
    public sealed class DiscoveredCoreAudioEndpoint
    {
        public string EndpointId { get; init; } = string.Empty;
        public string FriendlyName { get; init; } = string.Empty;
        public string InterfaceFriendlyName { get; init; } = string.Empty;
        public EDataFlow DataFlow { get; init; }
        public uint RawState { get; init; }
        public Guid? ContainerId { get; init; }
        public string? DeviceInstanceId { get; init; }
        public string? EnumeratorName { get; init; }
        public string? ExtractedBluetoothMac { get; init; }
        public int SelfActiveSessionCount { get; init; }
        public int ExternalActiveSessionCount { get; init; }
        public List<uint> ExternalActiveProcessIds { get; init; } = new();
    }

    public sealed class CoreAudioEndpointMapper : ICoreAudioEndpointMapper
    {
        private static readonly Regex MacRegex = new(
            @"(?:DEV_|&|_)([0-9A-Fa-f]{12})(?:_|&|\\|#|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public HeadsetAudioEndpointMap ResolveHeadsetEndpoints(string bluetoothMac, string deviceName)
        {
            var discovered = EnumerateActiveAndPresentEndpoints();
            return ResolveFromDiscoveredEndpoints(discovered, bluetoothMac, deviceName);
        }

        public static string? ExtractBluetoothMac(string? instanceOrAssociationPath)
        {
            if (string.IsNullOrWhiteSpace(instanceOrAssociationPath))
            {
                return null;
            }

            var match = MacRegex.Match(instanceOrAssociationPath);
            return match.Success ? match.Groups[1].Value.ToUpperInvariant() : null;
        }

        public static HeadsetAudioEndpointMap ResolveFromDiscoveredEndpoints(
            IReadOnlyList<DiscoveredCoreAudioEndpoint> endpoints,
            string bluetoothMac,
            string deviceName)
        {
            string cleanMac = (bluetoothMac ?? string.Empty).Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            var result = new HeadsetAudioEndpointMap
            {
                BluetoothMac = cleanMac,
                DeviceName = deviceName ?? string.Empty
            };

            if (endpoints == null || endpoints.Count == 0)
            {
                return result;
            }

            // Group endpoints by physical PKEY_Device_ContainerId first
            var byContainer = endpoints
                .Where(e => e.ContainerId.HasValue && e.ContainerId.Value != Guid.Empty)
                .GroupBy(e => e.ContainerId!.Value)
                .ToList();

            IGrouping<Guid, DiscoveredCoreAudioEndpoint>? matchedGroup = null;

            // 1. Primary deterministic match: Container group where any endpoint carries the exact 12-hex Bluetooth MAC
            if (!string.IsNullOrEmpty(cleanMac))
            {
                matchedGroup = byContainer.FirstOrDefault(g =>
                    g.Any(e => string.Equals(e.ExtractedBluetoothMac, cleanMac, StringComparison.OrdinalIgnoreCase)));
            }

            // 2. Secondary fallback: Unambiguous Bluetooth container matching the exact InterfaceFriendlyName
            if (matchedGroup == null && !string.IsNullOrWhiteSpace(deviceName))
            {
                var nameMatches = byContainer.Where(g =>
                    g.Any(e => IsBluetoothEnumerator(e) &&
                               string.Equals(e.InterfaceFriendlyName.Trim(), deviceName.Trim(), StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (nameMatches.Count == 1)
                {
                    matchedGroup = nameMatches[0];
                }
            }

            if (matchedGroup != null)
            {
                result.ContainerId = matchedGroup.Key;

                var activeRender = matchedGroup
                    .FirstOrDefault(e => e.DataFlow == EDataFlow.eRender && e.RawState == DEVICE_STATE_ACTIVE);
                var anyRender = activeRender ?? matchedGroup.FirstOrDefault(e => e.DataFlow == EDataFlow.eRender);

                var activeCapture = matchedGroup
                    .FirstOrDefault(e => e.DataFlow == EDataFlow.eCapture && e.RawState == DEVICE_STATE_ACTIVE);
                var anyCapture = activeCapture ?? matchedGroup.FirstOrDefault(e => e.DataFlow == EDataFlow.eCapture);

                if (anyRender != null)
                {
                    result.RenderEndpointId = anyRender.EndpointId;
                    result.IsRenderActive = anyRender.RawState == DEVICE_STATE_ACTIVE;
                }

                if (anyCapture != null)
                {
                    result.CaptureEndpointId = anyCapture.EndpointId;
                    result.IsCaptureActive = anyCapture.RawState == DEVICE_STATE_ACTIVE;
                    result.SelfActiveCaptureSessionCount = anyCapture.SelfActiveSessionCount;
                    result.ExternalActiveCaptureSessionCount = anyCapture.ExternalActiveSessionCount;
                    result.ExternalCaptureProcessIds = anyCapture.ExternalActiveProcessIds.ToList();
                }

                return result;
            }

            // 3. Fallback for endpoints missing ContainerId that still carry the exact MAC
            if (!string.IsNullOrEmpty(cleanMac))
            {
                var macEndpoints = endpoints
                    .Where(e => string.Equals(e.ExtractedBluetoothMac, cleanMac, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var renderEp = macEndpoints.FirstOrDefault(e => e.DataFlow == EDataFlow.eRender && e.RawState == DEVICE_STATE_ACTIVE)
                               ?? macEndpoints.FirstOrDefault(e => e.DataFlow == EDataFlow.eRender);
                var captureEp = macEndpoints.FirstOrDefault(e => e.DataFlow == EDataFlow.eCapture && e.RawState == DEVICE_STATE_ACTIVE)
                                ?? macEndpoints.FirstOrDefault(e => e.DataFlow == EDataFlow.eCapture);

                if (renderEp != null)
                {
                    result.RenderEndpointId = renderEp.EndpointId;
                    result.IsRenderActive = renderEp.RawState == DEVICE_STATE_ACTIVE;
                }

                if (captureEp != null)
                {
                    result.CaptureEndpointId = captureEp.EndpointId;
                    result.IsCaptureActive = captureEp.RawState == DEVICE_STATE_ACTIVE;
                    result.SelfActiveCaptureSessionCount = captureEp.SelfActiveSessionCount;
                    result.ExternalActiveCaptureSessionCount = captureEp.ExternalActiveSessionCount;
                    result.ExternalCaptureProcessIds = captureEp.ExternalActiveProcessIds.ToList();
                }
            }

            return result;
        }

        private static bool IsBluetoothEnumerator(DiscoveredCoreAudioEndpoint ep)
        {
            string combined = $"{ep.EnumeratorName} {ep.DeviceInstanceId}";
            return combined.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                   combined.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase);
        }

        private static List<DiscoveredCoreAudioEndpoint> EnumerateActiveAndPresentEndpoints()
        {
            var results = new List<DiscoveredCoreAudioEndpoint>();
            IMMDeviceEnumerator? enumerator = null;
            IMMDeviceCollection? collection = null;

            try
            {
                var comType = typeof(MMDeviceEnumeratorComObject);
                enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(comType)!;

                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eAll, DEVICE_STATEMASK_ALL, out collection);
                if (hr != S_OK || collection == null)
                {
                    return results;
                }

                if (collection.GetCount(out uint count) != S_OK)
                {
                    return results;
                }

                for (uint i = 0; i < count; i++)
                {
                    if (collection.Item(i, out var device) == S_OK && device != null)
                    {
                        try
                        {
                            var ep = InspectEndpoint(device);
                            if (ep != null)
                            {
                                results.Add(ep);
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(device);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"CoreAudioEndpointMapper enumeration warning: {ex.Message}");
            }
            finally
            {
                if (collection != null)
                {
                    try { Marshal.ReleaseComObject(collection); } catch { }
                }
                if (enumerator != null)
                {
                    try { Marshal.ReleaseComObject(enumerator); } catch { }
                }
            }

            return results;
        }

        private static DiscoveredCoreAudioEndpoint? InspectEndpoint(IMMDevice device)
        {
            if (device.GetId(out string endpointId) != S_OK || string.IsNullOrEmpty(endpointId))
            {
                return null;
            }

            device.GetState(out uint rawState);

            EDataFlow flow = EDataFlow.eRender;
            if (device is IMMEndpoint mmEndpoint)
            {
                mmEndpoint.GetDataFlow(out flow);
            }

            string friendlyName = string.Empty;
            string interfaceFriendlyName = string.Empty;
            Guid? containerId = null;
            string? instanceId = null;
            string? enumeratorName = null;

            if (device.OpenPropertyStore(STGM_READ, out var store) == S_OK && store != null)
            {
                try
                {
                    friendlyName = ReadPropertyString(store, PKEY_Device_FriendlyName) ?? string.Empty;
                    interfaceFriendlyName = ReadPropertyString(store, PKEY_DeviceInterface_FriendlyName) ?? string.Empty;
                    containerId = ReadPropertyGuid(store, PKEY_Device_ContainerId);
                    instanceId = ReadPropertyString(store, PKEY_Device_InstanceId)
                                 ?? ReadPropertyString(store, PKEY_AudioEndpoint_Interface)
                                 ?? ReadPropertyString(store, PKEY_AudioEndpoint_Association);
                    enumeratorName = ReadPropertyString(store, PKEY_Device_EnumeratorName);
                }
                finally
                {
                    Marshal.ReleaseComObject(store);
                }
            }

            int selfSessions = 0;
            int externalSessions = 0;
            var externalPids = new List<uint>();

            // Only query active sessions on active Capture endpoints to keep discovery fast and lightweight
            if (rawState == DEVICE_STATE_ACTIVE && flow == EDataFlow.eCapture)
            {
                QueryActiveSessionCounts(device, out selfSessions, out externalSessions, externalPids);
            }

            return new DiscoveredCoreAudioEndpoint
            {
                EndpointId = endpointId,
                FriendlyName = friendlyName,
                InterfaceFriendlyName = interfaceFriendlyName,
                DataFlow = flow,
                RawState = rawState,
                ContainerId = containerId,
                DeviceInstanceId = instanceId,
                EnumeratorName = enumeratorName,
                ExtractedBluetoothMac = ExtractBluetoothMac(instanceId),
                SelfActiveSessionCount = selfSessions,
                ExternalActiveSessionCount = externalSessions,
                ExternalActiveProcessIds = externalPids
            };
        }

        private static void QueryActiveSessionCounts(
            IMMDevice device,
            out int selfActiveCount,
            out int externalActiveCount,
            List<uint> externalPids)
        {
            selfActiveCount = 0;
            externalActiveCount = 0;

            Guid iidSessionMgr2 = IID_IAudioSessionManager2;
            if (device.Activate(ref iidSessionMgr2, CLSCTX_ALL, IntPtr.Zero, out object mgrObj) != S_OK || mgrObj == null)
            {
                return;
            }

            var mgr = (IAudioSessionManager2)mgrObj;
            try
            {
                if (mgr.GetSessionEnumerator(out var sessionEnum) != S_OK || sessionEnum == null)
                {
                    return;
                }

                try
                {
                    if (sessionEnum.GetCount(out int count) != S_OK)
                    {
                        return;
                    }

                    uint currentPid = (uint)Environment.ProcessId;

                    for (int i = 0; i < count; i++)
                    {
                        if (sessionEnum.GetSession(i, out var sessionCtrl) == S_OK && sessionCtrl != null)
                        {
                            try
                            {
                                if (sessionCtrl.GetState(out var state) == S_OK &&
                                    state == AudioSessionState.AudioSessionStateActive)
                                {
                                    if (sessionCtrl is IAudioSessionControl2 ctrl2 &&
                                        ctrl2.GetProcessId(out uint pid) == S_OK)
                                    {
                                        if (pid == currentPid)
                                        {
                                            selfActiveCount++;
                                        }
                                        else
                                        {
                                            externalActiveCount++;
                                            if (pid != 0 && !externalPids.Contains(pid))
                                            {
                                                externalPids.Add(pid);
                                            }
                                        }
                                    }
                                    else
                                    {
                                        externalActiveCount++;
                                    }
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(sessionCtrl);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(sessionEnum);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(mgr);
            }
        }
    }
}
