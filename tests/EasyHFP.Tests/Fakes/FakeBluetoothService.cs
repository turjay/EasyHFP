using System;
using System.Collections.Generic;
using System.Linq;
using EasyHFP.Interop;
using EasyHFP.Models;
using EasyHFP.Services;

namespace EasyHFP.Tests.Fakes
{
    public class FakeBluetoothService : IBluetoothService
    {
        public List<BtDeviceItem> Devices { get; set; } = new();
        public bool QueryThrowsError { get; set; } = false;
        public bool ServiceCallSucceeds { get; set; } = true;
        public bool VerificationSucceedsImmediately { get; set; } = true;
        public bool PnpRecoverySucceeds { get; set; } = true;
        public bool RadioResetSucceeds { get; set; } = true;

        public int MutationCount { get; private set; } = 0;
        public int ServiceCallCount { get; private set; } = 0;
        public int PnpResetCount { get; private set; } = 0;
        public int RadioResetCount { get; private set; } = 0;
        public BluetoothDeviceSnapshot? LastExecutedSnapshot { get; private set; }

        public bool IsAdministrator() => true;

        public List<BtDeviceItem> EnumerateDevices()
        {
            return Devices.Select(d => new BtDeviceItem
            {
                Name = d.Name,
                Mac = d.Mac,
                MacUlong = d.MacUlong,
                Connected = d.Connected,
                HfpState = d.HfpState,
                SupportsHfp = d.SupportsHfp,
                DeviceClass = d.DeviceClass,
                Info = d.Info
            }).ToList();
        }

        public HfpServiceState QueryHfpState(ref NativeBluetooth.BLUETOOTH_DEVICE_INFO devInfo)
        {
            if (QueryThrowsError)
            {
                return HfpServiceState.Unknown;
            }

            string mac = devInfo.Address.ullLong.ToString("X12");
            var dev = Devices.FirstOrDefault(d => d.Mac.Equals(mac, StringComparison.OrdinalIgnoreCase));
            return dev?.HfpState ?? HfpServiceState.Unknown;
        }

        public BtDeviceItem? FindDevice(string identifier, List<BtDeviceItem> devices, out string? ambiguityError)
        {
            ambiguityError = null;
            if (string.IsNullOrWhiteSpace(identifier) || devices == null || devices.Count == 0)
            {
                return null;
            }

            string clean = identifier.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
            var macMatch = devices.FirstOrDefault(d => d.Mac.Equals(clean, StringComparison.OrdinalIgnoreCase));
            if (macMatch != null) return macMatch;

            var exactMatches = devices.Where(d => d.Name.Equals(identifier.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            if (exactMatches.Count == 1) return exactMatches[0];
            if (exactMatches.Count > 1)
            {
                ambiguityError = $"Ambiguous device name '{identifier}'. Multiple devices share this exact name: {string.Join(", ", exactMatches.Select(d => $"{d.Name} [{d.Mac}]"))}.";
                return null;
            }

            var subMatches = devices.Where(d => d.Name.IndexOf(identifier.Trim(), StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            if (subMatches.Count == 1) return subMatches[0];
            if (subMatches.Count > 1)
            {
                ambiguityError = $"Ambiguous device match for '{identifier}'. Found {subMatches.Count} matching devices: {string.Join(", ", subMatches.Select(d => $"{d.Name} [{d.Mac}]"))}.";
                return null;
            }

            return null;
        }

        public TransitionResult ExecuteTransition(BluetoothDeviceSnapshot snapshot)
        {
            MutationCount++;
            ServiceCallCount++;
            LastExecutedSnapshot = snapshot;

            var result = new TransitionResult
            {
                TargetMac = snapshot.Mac,
                TargetName = snapshot.Name,
                RequestedState = snapshot.TargetHfpState,
                ServiceChangeAttempted = true,
                ServiceChangeSuccess = ServiceCallSucceeds
            };

            if (!ServiceCallSucceeds)
            {
                result.Success = false;
                result.ServiceChangeErrorCode = 5; // e.g. Access Denied
                result.ErrorMessage = "BluetoothSetServiceState failed in fake.";
                return result;
            }

            result.VerificationAttempted = true;
            if (VerificationSucceedsImmediately)
            {
                result.VerificationPassed = true;
                result.FinalState = snapshot.TargetHfpState;
                result.Success = true;
                result.ReconnectStrategyUsed = "none (in-place service switch)";
                return result;
            }

            // If not verified immediately, test recovery behavior
            result.VerificationPassed = false;
            if (snapshot.ReconnectMode == ReconnectMode.Auto || snapshot.ReconnectMode == ReconnectMode.Pnp)
            {
                PnpResetCount++;
                result.ReconnectAttempted = true;
                result.TargetRecoveryAttempted = true;
                result.TargetRecoverySuccess = PnpRecoverySucceeds;
                result.ReconnectStrategyUsed = "targeted-pnp";
                result.FinalState = PnpRecoverySucceeds ? snapshot.TargetHfpState : snapshot.InitialHfpState;
                result.Success = PnpRecoverySucceeds;
                if (!PnpRecoverySucceeds)
                {
                    result.ErrorMessage = "PnP recovery failed in fake.";
                }
            }
            else if (snapshot.ReconnectMode == ReconnectMode.Radio)
            {
                RadioResetCount++;
                result.ReconnectAttempted = true;
                result.RadioResetAttempted = true;
                result.ReconnectStrategyUsed = "radio (explicit)";
                result.FinalState = RadioResetSucceeds ? snapshot.TargetHfpState : snapshot.InitialHfpState;
                result.Success = RadioResetSucceeds;
                if (!RadioResetSucceeds)
                {
                    result.ErrorMessage = "Radio recovery failed in fake.";
                }
            }
            else
            {
                result.ReconnectStrategyUsed = "none";
                result.FinalState = snapshot.InitialHfpState;
                result.Success = false;
                result.ErrorMessage = "Verification timed out with no recovery requested.";
            }

            return result;
        }

        public bool RestartBluetoothRadio(out string? error)
        {
            RadioResetCount++;
            if (RadioResetSucceeds)
            {
                error = null;
                return true;
            }
            error = "Simulated radio failure";
            return false;
        }
    }
}
