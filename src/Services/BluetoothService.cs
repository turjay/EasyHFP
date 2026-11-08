using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using EasyHFP.Interop;
using EasyHFP.Models;

namespace EasyHFP.Services
{
    public class BluetoothService : IBluetoothService
    {
        private readonly INativeBluetoothAdapter _nativeBt;
        private readonly INativePnpAdapter _nativePnp;
        private readonly IRadioController _radioController;
        private readonly Func<IAudioEndpointMonitor> _endpointMonitorFactory;
        private readonly IProcessLockProvider _lockProvider;

        public BluetoothService(
            INativeBluetoothAdapter? nativeBt = null,
            INativePnpAdapter? nativePnp = null,
            IRadioController? radioController = null,
            Func<IAudioEndpointMonitor>? endpointMonitorFactory = null,
            IProcessLockProvider? lockProvider = null)
        {
            _nativeBt = nativeBt ?? new Win32BluetoothAdapter();
            _nativePnp = nativePnp ?? new Win32PnpAdapter();
            _radioController = radioController ?? new WinRtRadioController();
            _endpointMonitorFactory = endpointMonitorFactory ?? (() => new CoreAudioEndpointMonitor());
            _lockProvider = lockProvider ?? new NamedMutexLockProvider();
        }

        public bool IsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public HfpServiceState QueryHfpState(ref NativeBluetooth.BLUETOOTH_DEVICE_INFO devInfo)
        {
            const uint ERROR_MORE_DATA = 234;
            const uint ERROR_SUCCESS = 0;

            for (int retry = 0; retry < 3; retry++)
            {
                uint pcServices = 0;
                uint queryRes = _nativeBt.EnumerateInstalledServices(IntPtr.Zero, ref devInfo, ref pcServices, null);

                // When querying size with null buffer, Windows returns ERROR_MORE_DATA (234) if services exist
                if (queryRes != ERROR_SUCCESS && queryRes != ERROR_MORE_DATA)
                {
                    return HfpServiceState.Unknown;
                }

                if (pcServices == 0)
                {
                    return HfpServiceState.Disabled;
                }

                var services = new Guid[pcServices];
                uint res = _nativeBt.EnumerateInstalledServices(IntPtr.Zero, ref devInfo, ref pcServices, services);

                if (res == ERROR_MORE_DATA)
                {
                    // Number of services increased between sizing and enumeration call; retry with new size
                    continue;
                }

                if (res != ERROR_SUCCESS)
                {
                    return HfpServiceState.Unknown;
                }

                for (int i = 0; i < pcServices && i < services.Length; i++)
                {
                    if (services[i].Equals(NativeBluetooth.HfpGuid))
                    {
                        return HfpServiceState.Enabled;
                    }
                }

                return HfpServiceState.Disabled;
            }

            return HfpServiceState.Unknown;
        }

        public List<BtDeviceItem> EnumerateDevices()
        {
            var list = new List<BtDeviceItem>();

            var searchParams = new NativeBluetooth.BLUETOOTH_DEVICE_SEARCH_PARAMS
            {
                dwSize = (uint)Marshal.SizeOf(typeof(NativeBluetooth.BLUETOOTH_DEVICE_SEARCH_PARAMS)),
                fReturnRemembered = true,
                fReturnConnected = true,
                fReturnUnknown = false,
                hRadio = IntPtr.Zero
            };

            var deviceInfo = new NativeBluetooth.BLUETOOTH_DEVICE_INFO
            {
                dwSize = (uint)Marshal.SizeOf(typeof(NativeBluetooth.BLUETOOTH_DEVICE_INFO))
            };

            IntPtr hFind = _nativeBt.FindFirstDevice(ref searchParams, ref deviceInfo);
            if (hFind != IntPtr.Zero)
            {
                try
                {
                    do
                    {
                        if (deviceInfo.Address.ullLong != 0 && !string.IsNullOrWhiteSpace(deviceInfo.szName))
                        {
                            var hfpState = QueryHfpState(ref deviceInfo);
                            bool isAudioDevice = ((deviceInfo.ulClassofDevice & 0x1F00) == 0x0400);
                            bool supportsHfp = isAudioDevice || (hfpState == HfpServiceState.Enabled);

                            list.Add(new BtDeviceItem
                            {
                                Name = deviceInfo.szName,
                                Mac = deviceInfo.Address.ullLong.ToString("X12"),
                                MacUlong = deviceInfo.Address.ullLong,
                                Connected = deviceInfo.fConnected,
                                HfpState = hfpState,
                                SupportsHfp = supportsHfp,
                                DeviceClass = deviceInfo.ulClassofDevice,
                                Info = deviceInfo
                            });
                        }

                        deviceInfo = new NativeBluetooth.BLUETOOTH_DEVICE_INFO
                        {
                            dwSize = (uint)Marshal.SizeOf(typeof(NativeBluetooth.BLUETOOTH_DEVICE_INFO))
                        };
                    } while (_nativeBt.FindNextDevice(hFind, ref deviceInfo));
                }
                finally
                {
                    _nativeBt.FindDeviceClose(hFind);
                }
            }

            return list;
        }

        public BtDeviceItem? FindDevice(string identifier, List<BtDeviceItem> devices, out string? ambiguityError)
        {
            ambiguityError = null;
            if (string.IsNullOrWhiteSpace(identifier) || devices == null || devices.Count == 0)
            {
                return null;
            }

            string clean = identifier.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();

            // 1. MAC match
            var macMatch = devices.FirstOrDefault(d => d.Mac.Equals(clean, StringComparison.OrdinalIgnoreCase));
            if (macMatch != null)
            {
                return macMatch;
            }

            // 2. Exact Name match (case-insensitive)
            var exactMatches = devices.Where(d => d.Name.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (exactMatches.Count == 1)
            {
                return exactMatches[0];
            }
            if (exactMatches.Count > 1)
            {
                ambiguityError = $"Ambiguous device name '{identifier}'. Multiple devices share this exact name: {string.Join(", ", exactMatches.Select(d => $"{d.Name} [{d.Mac}]"))}. Please specify the MAC address.";
                return null;
            }

            // 3. Substring Name match (case-insensitive)
            var subMatches = devices.Where(d => d.Name.IndexOf(identifier.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (subMatches.Count == 1)
            {
                return subMatches[0];
            }
            if (subMatches.Count > 1)
            {
                ambiguityError = $"Ambiguous device match for '{identifier}'. Found {subMatches.Count} matching devices: {string.Join(", ", subMatches.Select(d => $"{d.Name} [{d.Mac}]"))}. Please specify a more specific name or the MAC address.";
                return null;
            }

            return null;
        }

        // =========================================================================
        // LEGACY COMPATIBILITY AREA
        // Retained for historical/CLI compatibility only.
        // Normal GUI/Tray/Hotkey switching uses WindowsAudioProfileController (WASAPI)
        // and NEVER calls BluetoothSetServiceState, SetupAPI PnP restarts, or Radio restarts.
        // =========================================================================

        public TransitionResult ExecuteTransition(BluetoothDeviceSnapshot snapshot)
        {
            var result = new TransitionResult
            {
                TargetMac = snapshot.Mac,
                TargetName = snapshot.Name,
                RequestedState = snapshot.TargetHfpState
            };

            var totalStopwatch = Stopwatch.StartNew();

            // 1. Cross-process synchronization lock
            using (var lockHandle = _lockProvider.TryAcquire(timeoutMs: 8000, out bool lockAcquired))
            {
                if (!lockAcquired)
                {
                    result.Success = false;
                    result.ErrorMessage = "Another EasyHFP operation or instance is currently running and holding the lock.";
                    result.TotalDuration = totalStopwatch.Elapsed;
                    return result;
                }

                var devInfo = snapshot.DeviceInfo;

                // 2. Re-read fresh current state AFTER lock acquisition to prevent stale decision races
                var freshHfpState = QueryHfpState(ref devInfo);
                if (freshHfpState == HfpServiceState.Unknown)
                {
                    result.Success = false;
                    result.FinalState = HfpServiceState.Unknown;
                    result.ErrorMessage = "Unable to determine current Bluetooth HFP service state (query returned Unknown). Aborting transition to protect device state.";
                    result.TotalDuration = totalStopwatch.Elapsed;
                    return result;
                }

                // 3. Calculate target state from User Intent and fresh state
                HfpServiceState actualTargetState;
                if (snapshot.Intent == TransitionIntent.Enable)
                {
                    if (freshHfpState == HfpServiceState.Enabled)
                    {
                        // Idempotent: already in target state
                        result.Success = true;
                        result.FinalState = HfpServiceState.Enabled;
                        result.RequestedState = HfpServiceState.Enabled;
                        result.ReconnectStrategyUsed = "none (already in target state)";
                        result.TotalDuration = totalStopwatch.Elapsed;
                        return result;
                    }
                    actualTargetState = HfpServiceState.Enabled;
                }
                else if (snapshot.Intent == TransitionIntent.Disable)
                {
                    if (freshHfpState == HfpServiceState.Disabled)
                    {
                        // Idempotent: already in target state
                        result.Success = true;
                        result.FinalState = HfpServiceState.Disabled;
                        result.RequestedState = HfpServiceState.Disabled;
                        result.ReconnectStrategyUsed = "none (already in target state)";
                        result.TotalDuration = totalStopwatch.Elapsed;
                        return result;
                    }
                    actualTargetState = HfpServiceState.Disabled;
                }
                else // Toggle
                {
                    actualTargetState = (freshHfpState == HfpServiceState.Enabled)
                        ? HfpServiceState.Disabled
                        : HfpServiceState.Enabled;
                }

                result.RequestedState = actualTargetState;

                // 4. Subscribe to Core Audio endpoint notifications BEFORE issuing service change
                using var endpointMonitor = _endpointMonitorFactory();

                // 5. In-place Win32 Bluetooth service state toggle
                uint targetFlag = (actualTargetState == HfpServiceState.Enabled) ? 1u : 0u;
                Guid hfpGuid = NativeBluetooth.HfpGuid;

                var serviceWatch = Stopwatch.StartNew();
                uint serviceRes = _nativeBt.SetServiceState(IntPtr.Zero, ref devInfo, ref hfpGuid, targetFlag);
                serviceWatch.Stop();

                result.ServiceCallDuration = serviceWatch.Elapsed;
                result.ServiceChangeAttempted = true;
                result.ServiceChangeErrorCode = serviceRes;
                result.ServiceChangeSuccess = (serviceRes == 0);

                if (serviceRes == 5) // ERROR_ACCESS_DENIED
                {
                    result.Success = false;
                    result.ErrorMessage = "Access Denied (Win32 Error 5) while toggling Bluetooth service state. Administrator privileges are required for this action.";
                    result.TotalDuration = totalStopwatch.Elapsed;
                    // Never perform blind resets on access denied!
                    return result;
                }
                else if (serviceRes != 0)
                {
                    result.ErrorMessage = $"BluetoothSetServiceState returned error code {serviceRes}.";
                }

                // 6. Fast bounded verification loop using endpoint monitor as wake-up signal
                var verifyWatch = Stopwatch.StartNew();
                bool stateAchieved = false;

                // Max 1500ms verification polling (checking every 50ms or immediately on notification)
                int maxIterations = 30;
                for (int i = 0; i < maxIterations; i++)
                {
                    var current = QueryHfpState(ref devInfo);
                    if (current == actualTargetState)
                    {
                        stateAchieved = true;
                        result.FinalState = current;
                        break;
                    }

                    endpointMonitor.WaitForChange(50);
                }
                verifyWatch.Stop();

                result.VerificationDuration = verifyWatch.Elapsed;
                result.VerificationAttempted = true;
                result.VerificationPassed = stateAchieved;

                // 7. Success path: Minimal-intervention
                if (stateAchieved && result.ServiceChangeSuccess)
                {
                    result.Success = true;
                    result.ReconnectStrategyUsed = "none (in-place service switch)";
                    result.TotalDuration = totalStopwatch.Elapsed;
                    return result;
                }

                // 8. If headset is currently disconnected (!snapshot.Connected), do NOT trigger PnP reset
                if (!snapshot.Connected)
                {
                    var currentDisconnectedState = QueryHfpState(ref devInfo);
                    result.FinalState = currentDisconnectedState;
                    if (currentDisconnectedState == actualTargetState && result.ServiceChangeSuccess)
                    {
                        result.Success = true;
                        result.ReconnectStrategyUsed = "none (device disconnected, service updated for next connection)";
                        result.TotalDuration = totalStopwatch.Elapsed;
                        return result;
                    }
                }

                // 9. Targeted Recovery ONLY when verification was not achieved for connected device
                var recoveryWatch = Stopwatch.StartNew();

                if (snapshot.ReconnectMode == ReconnectMode.Auto || snapshot.ReconnectMode == ReconnectMode.Pnp)
                {
                    result.ReconnectAttempted = true;
                    result.ReconnectStrategyUsed = "targeted-pnp";

                    // Restart ONLY verified target device's PnP nodes
                    var pnpResult = _nativePnp.RestartDeviceNodes(snapshot.Mac, waitMs: 600);
                    result.TargetRecoveryAttempted = true;
                    result.TargetRecoverySuccess = pnpResult.Success;

                    if (!pnpResult.Success && !string.IsNullOrEmpty(pnpResult.ErrorMessage))
                    {
                        result.DiagnosticDetails = pnpResult.ErrorMessage;
                    }
                }
                else if (snapshot.ReconnectMode == ReconnectMode.Radio)
                {
                    // Master radio reset ONLY if explicitly configured by the user
                    result.ReconnectAttempted = true;
                    result.RadioResetAttempted = true;
                    result.ReconnectStrategyUsed = "radio (explicit)";

                    bool radioSuccess = _radioController.RestartBluetoothRadio(out string? radioErr);
                    result.TargetRecoverySuccess = radioSuccess;
                    if (!radioSuccess && radioErr != null)
                    {
                        result.DiagnosticDetails = radioErr;
                    }
                }
                else
                {
                    result.ReconnectStrategyUsed = "none";
                }

                recoveryWatch.Stop();
                result.RecoveryDuration = recoveryWatch.Elapsed;

                // 10. Post-recovery verification
                var finalState = QueryHfpState(ref devInfo);
                result.FinalState = finalState;
                result.Success = (finalState == actualTargetState) && (!result.TargetRecoveryAttempted || result.TargetRecoverySuccess);

                if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = $"Failed to switch HFP to {actualTargetState}. Verified final state is: {finalState}.";
                }

                result.TotalDuration = totalStopwatch.Elapsed;
                return result;
            }
        }

        public bool RestartBluetoothRadio(out string? error)
        {
            return _radioController.RestartBluetoothRadio(out error);
        }
    }
}
