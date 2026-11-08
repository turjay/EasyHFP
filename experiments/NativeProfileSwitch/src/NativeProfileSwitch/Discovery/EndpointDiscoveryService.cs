using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using NativeProfileSwitch.Interop;
using NativeProfileSwitch.Models;
using static NativeProfileSwitch.Interop.CoreAudioInterop;

namespace NativeProfileSwitch.Discovery
{
    public sealed class EndpointDiscoveryService
    {
        private static readonly Regex MacRegex = new(
            @"(?:DEV_|&|_)([0-9A-Fa-f]{12})(?:_|&|\\|#|$)",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>
        /// Enumerates Windows Core Audio endpoints (read-only) without opening or starting any audio streams.
        /// </summary>
        public List<AudioEndpointInfo> EnumerateAllEndpoints(
            uint stateMask = DEVICE_STATEMASK_ALL,
            bool queryMixFormatCapabilities = true,
            bool probeCommunicationsMixFormat = false)
        {
            var results = new List<AudioEndpointInfo>();
            var comType = typeof(MMDeviceEnumeratorComObject);
            var enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(comType)!;

            try
            {
                var defaultRolesByEndpoint = QueryDefaultEndpointRoles(enumerator);

                int hr = enumerator.EnumAudioEndpoints(EDataFlow.eAll, stateMask, out var collection);
                if (hr != S_OK || collection == null)
                {
                    return results;
                }

                try
                {
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
                                var info = InspectDeviceReadOnly(device, defaultRolesByEndpoint, queryMixFormatCapabilities, probeCommunicationsMixFormat);
                                if (info != null)
                                {
                                    results.Add(info);
                                }
                            }
                            finally
                            {
                                Marshal.ReleaseComObject(device);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(collection);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(enumerator);
            }

            return results;
        }

        private static Dictionary<string, List<string>> QueryDefaultEndpointRoles(IMMDeviceEnumerator enumerator)
        {
            var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            EDataFlow[] flows = { EDataFlow.eRender, EDataFlow.eCapture };
            ERole[] roles = { ERole.eConsole, ERole.eMultimedia, ERole.eCommunications };

            foreach (var flow in flows)
            {
                foreach (var role in roles)
                {
                    if (enumerator.GetDefaultAudioEndpoint(flow, role, out var dev) == S_OK && dev != null)
                    {
                        try
                        {
                            if (dev.GetId(out string id) == S_OK && !string.IsNullOrEmpty(id))
                            {
                                if (!map.TryGetValue(id, out var list))
                                {
                                    list = new List<string>();
                                    map[id] = list;
                                }
                                string roleLabel = $"{flow.ToString().Substring(1)}:{role.ToString().Substring(1)}";
                                if (!list.Contains(roleLabel))
                                {
                                    list.Add(roleLabel);
                                }
                            }
                        }
                        finally
                        {
                            Marshal.ReleaseComObject(dev);
                        }
                    }
                }
            }

            return map;
        }

        public static AudioEndpointInfo? InspectDeviceReadOnly(
            IMMDevice device,
            Dictionary<string, List<string>>? defaultRolesMap = null,
            bool queryMixFormatCapabilities = true,
            bool probeCommunicationsMixFormat = false)
        {
            if (device.GetId(out string endpointId) != S_OK || string.IsNullOrEmpty(endpointId))
            {
                return null;
            }

            device.GetState(out uint rawState);

            string dataFlowStr = "Unknown";
            if (device is IMMEndpoint mmEndpoint && mmEndpoint.GetDataFlow(out var flow) == S_OK)
            {
                dataFlowStr = flow == EDataFlow.eRender ? "Render" : (flow == EDataFlow.eCapture ? "Capture" : flow.ToString());
            }

            var info = new AudioEndpointInfo
            {
                EndpointId = endpointId,
                RawState = rawState,
                StateText = FormatEndpointState(rawState),
                DataFlow = dataFlowStr
            };

            if (defaultRolesMap != null && defaultRolesMap.TryGetValue(endpointId, out var roles))
            {
                info.DefaultRoles.AddRange(roles);
            }

            if (device.OpenPropertyStore(STGM_READ, out var store) == S_OK && store != null)
            {
                try
                {
                    info.FriendlyName = ReadPropertyString(store, PKEY_Device_FriendlyName) ?? string.Empty;
                    info.DeviceDescription = ReadPropertyString(store, PKEY_Device_DeviceDesc) ?? string.Empty;
                    info.InterfaceFriendlyName = ReadPropertyString(store, PKEY_DeviceInterface_FriendlyName) ?? string.Empty;
                    info.ContainerId = ReadPropertyGuid(store, PKEY_Device_ContainerId);
                    info.DeviceInstanceId = ReadPropertyString(store, PKEY_Device_InstanceId)
                        ?? ReadPropertyString(store, PKEY_AudioEndpoint_Interface)
                        ?? ReadPropertyString(store, PKEY_AudioEndpoint_Association);
                    info.EnumeratorName = ReadPropertyString(store, PKEY_Device_EnumeratorName);
                    info.FormFactor = ReadPropertyUint(store, PKEY_AudioEndpoint_FormFactor);
                    info.PhysicalSpeakersMask = ReadPropertyUint(store, PKEY_AudioEndpoint_PhysicalSpeakers);

                    var devFmt = ReadPropertyWaveFormat(store, PKEY_AudioEngine_DeviceFormat);
                    if (devFmt.HasValue)
                    {
                        info.PropertyStoreDeviceFormat = AudioFormatSummary.FromWaveFormat(devFmt.Value);
                    }

                    var oemFmt = ReadPropertyWaveFormat(store, PKEY_AudioEngine_OEMFormat);
                    if (oemFmt.HasValue)
                    {
                        info.PropertyStoreOemFormat = AudioFormatSummary.FromWaveFormat(oemFmt.Value);
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(store);
                }
            }

            ClassifyBluetoothProperties(info);

            // For active endpoints, inspect active sessions and supported mix formats without initializing a stream
            if (rawState == DEVICE_STATE_ACTIVE)
            {
                PopulateActiveSessionCounts(device, info);

                if (queryMixFormatCapabilities)
                {
                    PopulateMixFormatCapabilities(device, info, probeCommunicationsMixFormat);
                }
            }

            return info;
        }

        public static void ClassifyBluetoothProperties(AudioEndpointInfo info)
        {
            string combined = $"{info.DeviceInstanceId} {info.EnumeratorName} {info.FriendlyName} {info.InterfaceFriendlyName} {info.DeviceDescription}";

            if (!string.IsNullOrEmpty(info.DeviceInstanceId))
            {
                var m = MacRegex.Match(info.DeviceInstanceId);
                if (m.Success)
                {
                    info.ExtractedBluetoothMac = m.Groups[1].Value.ToUpperInvariant();
                }
            }

            bool hasBthLe = combined.Contains("BTHLE", StringComparison.OrdinalIgnoreCase) ||
                            combined.Contains("LEAudio", StringComparison.OrdinalIgnoreCase);
            bool hasBthHf = combined.Contains("BTHHFENUM", StringComparison.OrdinalIgnoreCase) ||
                            combined.Contains("Hands-Free", StringComparison.OrdinalIgnoreCase) ||
                            combined.Contains("0000111e", StringComparison.OrdinalIgnoreCase) ||
                            combined.Contains("00001108", StringComparison.OrdinalIgnoreCase);
            bool hasBthA2dp = combined.Contains("0000110b", StringComparison.OrdinalIgnoreCase);
            bool hasBthEnum = combined.Contains("BTHENUM", StringComparison.OrdinalIgnoreCase) ||
                              combined.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase);

            if (hasBthLe)
            {
                info.IsBluetoothLeAudio = true;
                info.IsBluetoothClassic = false;
                info.BluetoothTransportKind = "LE-Audio";
            }
            else if (hasBthHf)
            {
                info.IsBluetoothClassic = true;
                info.BluetoothTransportKind = "Classic-BthHfEnum";
            }
            else if (hasBthA2dp)
            {
                info.IsBluetoothClassic = true;
                info.BluetoothTransportKind = "Classic-BthA2dp";
            }
            else if (hasBthEnum)
            {
                info.IsBluetoothClassic = true;
                info.BluetoothTransportKind = "Classic-UnifiedOrBthEnum";
            }
            else
            {
                info.BluetoothTransportKind = "NonBluetooth";
            }
        }

        private static void PopulateActiveSessionCounts(IMMDevice device, AudioEndpointInfo info)
        {
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

                    info.TotalSessionCount = count;
                    uint currentPid = (uint)Environment.ProcessId;

                    for (int i = 0; i < count; i++)
                    {
                        if (sessionEnum.GetSession(i, out var sessionCtrl) == S_OK && sessionCtrl != null)
                        {
                            try
                            {
                                if (sessionCtrl.GetState(out var state) == S_OK && state == AudioSessionState.AudioSessionStateActive)
                                {
                                    info.ActiveSessionCount++;

                                    if (sessionCtrl is IAudioSessionControl2 ctrl2 && ctrl2.GetProcessId(out uint pid) == S_OK)
                                    {
                                        if (pid == currentPid)
                                        {
                                            info.SelfActiveSessionCount++;
                                        }
                                        else
                                        {
                                            info.ExternalActiveSessionCount++;
                                            if (pid != 0 && !info.ExternalActiveProcessIds.Contains(pid))
                                            {
                                                info.ExternalActiveProcessIds.Add(pid);
                                            }
                                        }
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

        private static void PopulateMixFormatCapabilities(IMMDevice device, AudioEndpointInfo info, bool probeCommunicationsMixFormat)
        {
            // 1. Default Mix Format (without SetClientProperties)
            Guid iidClient = IID_IAudioClient;
            if (device.Activate(ref iidClient, CLSCTX_ALL, IntPtr.Zero, out object clientObj) == S_OK && clientObj != null)
            {
                var client = (IAudioClient)clientObj;
                try
                {
                    if (client.GetMixFormat(out IntPtr fmtPtr) == S_OK && fmtPtr != IntPtr.Zero)
                    {
                        try
                        {
                            var wf = Marshal.PtrToStructure<WAVEFORMATEX>(fmtPtr);
                            info.DefaultMixFormat = AudioFormatSummary.FromWaveFormat(wf);
                        }
                        finally
                        {
                            CoTaskMemFree(fmtPtr);
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(client);
                }
            }

            if (!probeCommunicationsMixFormat)
            {
                return;
            }

            // 2. Communications Mix Format (IAudioClient2::SetClientProperties with AudioCategory_Communications before GetMixFormat)
            Guid iidClient2 = IID_IAudioClient2;
            if (device.Activate(ref iidClient2, CLSCTX_ALL, IntPtr.Zero, out object client2Obj) == S_OK && client2Obj != null)
            {
                var client2 = (IAudioClient2)client2Obj;
                try
                {
                    var props = new AudioClientProperties
                    {
                        cbSize = (uint)Marshal.SizeOf<AudioClientProperties>(),
                        bIsOffload = false,
                        eCategory = AUDIO_STREAM_CATEGORY.AudioCategory_Communications,
                        Options = AUDCLNT_STREAMOPTIONS.AUDCLNT_STREAMOPTIONS_NONE
                    };

                    if (client2.SetClientProperties(ref props) == S_OK)
                    {
                        if (client2.GetMixFormat(out IntPtr commFmtPtr) == S_OK && commFmtPtr != IntPtr.Zero)
                        {
                            try
                            {
                                var wf = Marshal.PtrToStructure<WAVEFORMATEX>(commFmtPtr);
                                info.CommunicationsMixFormat = AudioFormatSummary.FromWaveFormat(wf);
                            }
                            finally
                            {
                                CoTaskMemFree(commFmtPtr);
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.ReleaseComObject(client2);
                }
            }
        }

        /// <summary>
        /// Associates Render and Capture endpoints belonging to the same physical headset using
        /// hardware ContainerId (primary) or extracted Bluetooth MAC (secondary).
        /// Never merges endpoints purely by FriendlyName; flags name collisions as ambiguities.
        /// </summary>
        public List<HeadsetEndpointGroup> GroupEndpointsByPhysicalDevice(IEnumerable<AudioEndpointInfo> endpoints)
        {
            var groupsByKey = new Dictionary<string, HeadsetEndpointGroup>(StringComparer.OrdinalIgnoreCase);

            foreach (var ep in endpoints)
            {
                string groupKey;
                string method;

                if (ep.ContainerId.HasValue && ep.ContainerId.Value != Guid.Empty)
                {
                    groupKey = $"CONTAINER:{ep.ContainerId.Value:D}";
                    method = "PKEY_Device_ContainerId";
                }
                else if (!string.IsNullOrEmpty(ep.ExtractedBluetoothMac))
                {
                    groupKey = $"MAC:{ep.ExtractedBluetoothMac}";
                    method = "HardwareInstanceMac";
                }
                else
                {
                    groupKey = $"UNGROUPED:{ep.EndpointId}";
                    method = "IsolatedEndpoint (Missing ContainerId and MAC)";
                }

                if (!groupsByKey.TryGetValue(groupKey, out var group))
                {
                    string displayName = !string.IsNullOrWhiteSpace(ep.InterfaceFriendlyName)
                        ? ep.InterfaceFriendlyName
                        : ep.FriendlyName;

                    group = new HeadsetEndpointGroup
                    {
                        GroupKey = groupKey,
                        DisplayName = displayName,
                        ContainerId = ep.ContainerId,
                        BluetoothMac = ep.ExtractedBluetoothMac,
                        AssociationMethod = method,
                        IsBluetoothClassic = ep.IsBluetoothClassic,
                        IsBluetoothLeAudio = ep.IsBluetoothLeAudio
                    };
                    groupsByKey[groupKey] = group;
                }
                else
                {
                    if (string.IsNullOrEmpty(group.BluetoothMac) && !string.IsNullOrEmpty(ep.ExtractedBluetoothMac))
                    {
                        group.BluetoothMac = ep.ExtractedBluetoothMac;
                    }
                    group.IsBluetoothClassic |= ep.IsBluetoothClassic;
                    group.IsBluetoothLeAudio |= ep.IsBluetoothLeAudio;
                }

                if (ep.DataFlow.Equals("Render", StringComparison.OrdinalIgnoreCase))
                {
                    group.RenderEndpoints.Add(ep);
                }
                else if (ep.DataFlow.Equals("Capture", StringComparison.OrdinalIgnoreCase))
                {
                    group.CaptureEndpoints.Add(ep);
                }
            }

            var allGroups = groupsByKey.Values.ToList();

            // Check topology within each group and detect cross-group friendly name ambiguities
            var nameCounts = allGroups
                .Where(g => !string.IsNullOrWhiteSpace(g.DisplayName))
                .GroupBy(g => g.DisplayName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

            foreach (var group in allGroups)
            {
                int activeRender = group.RenderEndpoints.Count(r => r.RawState == DEVICE_STATE_ACTIVE);
                int activeCapture = group.CaptureEndpoints.Count(c => c.RawState == DEVICE_STATE_ACTIVE);

                // On Windows 11, a Bluetooth Classic headset presents 1 unified active Render endpoint + 1 active Capture endpoint.
                // On Windows 10, it typically presents 2 active Render endpoints (Stereo + Hands-Free AG Audio) under the same ContainerId.
                group.IsWindows11UnifiedEndpointTopology = group.IsBluetoothClassic && activeRender == 1 && activeCapture == 1;

                var ambiguityNotes = new List<string>();
                if (activeRender > 1)
                {
                    ambiguityNotes.Add(
                        $"Container exposes {activeRender} active Render endpoints ({string.Join(", ", group.RenderEndpoints.Where(r => r.RawState == DEVICE_STATE_ACTIVE).Select(r => r.FriendlyName))}). " +
                        "This indicates a split A2DP/HFP endpoint topology (typical on Windows 10 or non-unified drivers).");
                }

                if (group.IsBluetoothClassic && activeRender >= 1 && group.CaptureEndpoints.Count == 0)
                {
                    ambiguityNotes.Add(
                        "Bluetooth Classic device has an active Render endpoint but 0 Capture endpoints in MMDEVAPI. " +
                        "If this is a headset with a microphone, its Hands-Free Profile (HFP, 0000111e-0000-1000-8000-00805f9b34fb) " +
                        "Bluetooth service may have been disabled via BluetoothSetServiceState (e.g., by legacy EasyHFP). " +
                        "Both A2DP (0000110b) and HFP (0000111e) services must remain enabled in Windows for native WASAPI stream-driven switching.");
                }

                if (!string.IsNullOrWhiteSpace(group.DisplayName) &&
                    nameCounts.TryGetValue(group.DisplayName.Trim(), out int sameNameCount) &&
                    sameNameCount > 1)
                {
                    group.HasAmbiguity = true;
                    ambiguityNotes.Add(
                        $"Multiple distinct physical containers ({sameNameCount}) share the display name '{group.DisplayName}'. " +
                        "Never match by friendly name alone; disambiguate using ContainerId or EndpointId.");
                }

                if (group.AssociationMethod.StartsWith("Isolated", StringComparison.Ordinal))
                {
                    group.HasAmbiguity = true;
                    ambiguityNotes.Add("Endpoint lacked both PKEY_Device_ContainerId and a parseable hardware MAC address.");
                }

                if (ambiguityNotes.Count > 0)
                {
                    group.AmbiguityNotes = string.Join(" | ", ambiguityNotes);
                }
            }

            return allGroups;
        }
    }
}
