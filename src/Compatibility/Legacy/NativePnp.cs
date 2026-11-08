using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace EasyHFP.Interop
{
    public static class NativePnp
    {
        public const uint DIGCF_ALLCLASSES = 0x00000004;
        public const uint DIGCF_PRESENT = 0x00000002;
        public const uint DIF_PROPERTYCHANGE = 0x00000012;
        public const uint DICS_ENABLE = 0x00000001;
        public const uint DICS_DISABLE = 0x00000002;
        public const uint DICS_FLAG_GLOBAL = 0x00000001;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_CLASSINSTALL_HEADER
        {
            public uint cbSize;
            public uint InstallFunction;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_PROPCHANGE_PARAMS
        {
            public SP_CLASSINSTALL_HEADER ClassInstallHeader;
            public uint StateChange;
            public uint Scope;
            public uint HwProfile;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string? Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, int DeviceInstanceIdSize, out int RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiSetClassInstallParams(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref SP_PROPCHANGE_PARAMS ClassInstallParams, int ClassInstallParamsSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiCallClassInstaller(uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        public class PnpRestartResult
        {
            public bool Success { get; set; }
            public int NodesFound { get; set; }
            public int NodesDisabled { get; set; }
            public int NodesEnabled { get; set; }
            public List<string> NodesDisabledInstanceIds { get; set; } = new();
            public List<string> NodesReenabledInstanceIds { get; set; } = new();
            public List<string> NodesFailedToReenableInstanceIds { get; set; } = new();
            public string? ErrorMessage { get; set; }
            public int LastWin32Error { get; set; }
        }

        /// <summary>
        /// Restarts ONLY the verified PnP device nodes for the target MAC address under BTHENUM.
        /// Includes rollback cleanup if disabling or enabling fails.
        /// </summary>
        public static PnpRestartResult RestartDeviceNodes(string macHex, int waitMs = 800)
        {
            var result = new PnpRestartResult();
            if (string.IsNullOrWhiteSpace(macHex) || macHex.Length != 12)
            {
                result.ErrorMessage = $"Invalid MAC format for PnP restart: {macHex}";
                return result;
            }

            IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "BTHENUM", IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
            if (devInfoSet == (IntPtr)(-1) || devInfoSet == IntPtr.Zero)
            {
                result.LastWin32Error = Marshal.GetLastWin32Error();
                result.ErrorMessage = $"SetupDiGetClassDevs failed with Win32 error {result.LastWin32Error}";
                return result;
            }

            var matchedDevices = new List<(SP_DEVINFO_DATA dev, string instanceId)>();
            string targetPrefix = $"BTHENUM\\DEV_{macHex}".ToUpperInvariant();

            try
            {
                var devInfoData = new SP_DEVINFO_DATA();
                devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                uint index = 0;

                while (SetupDiEnumDeviceInfo(devInfoSet, index, ref devInfoData))
                {
                    var sb = new StringBuilder(512);
                    if (SetupDiGetDeviceInstanceId(devInfoSet, ref devInfoData, sb, sb.Capacity, out _))
                    {
                        string instanceId = sb.ToString().ToUpperInvariant();
                        if (instanceId.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase))
                        {
                            matchedDevices.Add((devInfoData, instanceId));
                        }
                    }
                    index++;
                    devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                }

                result.NodesFound = matchedDevices.Count;
                if (matchedDevices.Count == 0)
                {
                    result.ErrorMessage = $"No active PnP nodes found matching '{targetPrefix}'";
                    return result;
                }

                // Phase 1: Disable nodes (track only those successfully disabled by EasyHFP)
                var disabledNodes = new List<(SP_DEVINFO_DATA dev, string instanceId)>();
                foreach (var item in matchedDevices)
                {
                    if (SetDeviceState(devInfoSet, item.dev, DICS_DISABLE))
                    {
                        disabledNodes.Add(item);
                        result.NodesDisabled++;
                        result.NodesDisabledInstanceIds.Add(item.instanceId);
                    }
                    else
                    {
                        result.LastWin32Error = Marshal.GetLastWin32Error();
                    }
                }

                // Bounded wait between disable and enable
                if (waitMs > 0)
                {
                    Thread.Sleep(waitMs);
                }

                // Phase 2: Enable nodes (including retry for any node that failed to enable)
                var pendingNodes = new List<(SP_DEVINFO_DATA dev, string instanceId)>(disabledNodes);
                for (int attempt = 0; attempt < 3 && pendingNodes.Count > 0; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(300);

                    for (int i = pendingNodes.Count - 1; i >= 0; i--)
                    {
                        var item = pendingNodes[i];
                        if (SetDeviceState(devInfoSet, item.dev, DICS_ENABLE))
                        {
                            result.NodesEnabled++;
                            result.NodesReenabledInstanceIds.Add(item.instanceId);
                            pendingNodes.RemoveAt(i);
                        }
                        else
                        {
                            result.LastWin32Error = Marshal.GetLastWin32Error();
                        }
                    }
                }

                foreach (var remaining in pendingNodes)
                {
                    result.NodesFailedToReenableInstanceIds.Add(remaining.instanceId);
                }

                result.Success = (result.NodesDisabled > 0) && (pendingNodes.Count == 0);
                if (!result.Success && string.IsNullOrEmpty(result.ErrorMessage))
                {
                    result.ErrorMessage = $"PnP node reset incomplete. Disabled: {result.NodesDisabled}/{result.NodesFound}, Re-enabled: {result.NodesEnabled}/{disabledNodes.Count}. Failed to re-enable: {string.Join(", ", result.NodesFailedToReenableInstanceIds)}. Last Win32 error: {result.LastWin32Error}";
                }
                return result;
            }
            catch (Exception ex)
            {
                result.ErrorMessage = $"Exception during PnP restart: {ex.Message}";
                return result;
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfoSet);
            }
        }

        private static bool SetDeviceState(IntPtr devInfoSet, SP_DEVINFO_DATA devInfoData, uint stateChange)
        {
            var pcp = new SP_PROPCHANGE_PARAMS
            {
                ClassInstallHeader = new SP_CLASSINSTALL_HEADER
                {
                    cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER)),
                    InstallFunction = DIF_PROPERTYCHANGE
                },
                StateChange = stateChange,
                Scope = DICS_FLAG_GLOBAL,
                HwProfile = 0
            };

            var devCopy = devInfoData;
            if (!SetupDiSetClassInstallParams(devInfoSet, ref devCopy, ref pcp, Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS))))
            {
                return false;
            }

            return SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devInfoSet, ref devCopy);
        }
    }
}
