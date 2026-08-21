using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using Windows.Data.Xml.Dom;
using Windows.Devices.Radios;
using Windows.UI.Notifications;

namespace EasyHFP
{
    internal class Program
    {
        #region Win32 P/Invoke & High DPI

        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_ADDRESS
        {
            public ulong ullLong;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct BLUETOOTH_DEVICE_INFO
        {
            public uint dwSize;
            public BLUETOOTH_ADDRESS Address;
            public uint ulClassofDevice;
            public bool fConnected;
            public bool fRemembered;
            public bool fAuthenticated;
            public SYSTEMTIME stLastSeen;
            public SYSTEMTIME stLastUsed;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SYSTEMTIME
        {
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public uint dwSize;
            public bool fReturnAuthenticated;
            public bool fReturnRemembered;
            public bool fReturnUnknown;
            public bool fReturnConnected;
            public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern uint BluetoothSetServiceState(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref Guid pGuidService, uint dwFlags);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern uint BluetoothEnumerateInstalledServices(IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtdi, ref uint pcServices, [In, Out] Guid[] pGuidServices);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(int dwProcessId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetStdHandle(int nStdHandle);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = (IntPtr)(-4);
        private const int ATTACH_PARENT_PROCESS = -1;
        private const int STD_OUTPUT_HANDLE = -11;

        private static void EnableHighDpiAwareness()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 10)
                {
                    SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
                }
                else
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                try { SetProcessDPIAware(); } catch { }
            }
        }

        private static void AttachParentConsole()
        {
            if (AttachConsole(ATTACH_PARENT_PROCESS))
            {
                try
                {
                    IntPtr stdHandle = GetStdHandle(STD_OUTPUT_HANDLE);
                    Microsoft.Win32.SafeHandles.SafeFileHandle safeFileHandle = new Microsoft.Win32.SafeHandles.SafeFileHandle(stdHandle, false);
                    FileStream fileStream = new FileStream(safeFileHandle, FileAccess.Write);
                    StreamWriter standardOutput = new StreamWriter(fileStream, Encoding.UTF8);
                    standardOutput.AutoFlush = true;
                    Console.SetOut(standardOutput);
                    Console.SetError(standardOutput);
                }
                catch { }
            }
        }

        private static Guid HfpGuid = new Guid("0000111e-0000-1000-8000-00805f9b34fb");

        #endregion

        #region Native Win32 SetupAPI PnP Manager

        public static class NativePnpManager
        {
            private const uint DIGCF_ALLCLASSES = 0x00000004;
            private const uint DIGCF_PRESENT = 0x00000002;
            private const uint DIF_PROPERTYCHANGE = 0x00000012;
            private const uint DICS_ENABLE = 0x00000001;
            private const uint DICS_DISABLE = 0x00000002;
            private const uint DICS_FLAG_GLOBAL = 0x00000001;

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
            private static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

            [DllImport("setupapi.dll", SetLastError = true)]
            private static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
            private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, int DeviceInstanceIdSize, out int RequiredSize);

            [DllImport("setupapi.dll", SetLastError = true)]
            private static extern bool SetupDiSetClassInstallParams(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, ref SP_PROPCHANGE_PARAMS ClassInstallParams, int ClassInstallParamsSize);

            [DllImport("setupapi.dll", SetLastError = true)]
            private static extern bool SetupDiCallClassInstaller(uint InstallFunction, IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData);

            [DllImport("setupapi.dll", SetLastError = true)]
            private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

            public static bool RestartDeviceNodes(string macHex, double waitSeconds)
            {
                IntPtr devInfoSet = SetupDiGetClassDevs(IntPtr.Zero, "BTHENUM", IntPtr.Zero, DIGCF_ALLCLASSES | DIGCF_PRESENT);
                if (devInfoSet == (IntPtr)(-1) || devInfoSet == IntPtr.Zero)
                {
                    return false;
                }

                List<SP_DEVINFO_DATA> matchedDevices = new List<SP_DEVINFO_DATA>();
                string targetPrefix = string.Format("BTHENUM\\DEV_{0}", macHex).ToUpperInvariant();

                try
                {
                    SP_DEVINFO_DATA devInfoData = new SP_DEVINFO_DATA();
                    devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    uint index = 0;

                    while (SetupDiEnumDeviceInfo(devInfoSet, index, ref devInfoData))
                    {
                        StringBuilder sb = new StringBuilder(512);
                        int reqSize = 0;
                        if (SetupDiGetDeviceInstanceId(devInfoSet, ref devInfoData, sb, sb.Capacity, out reqSize))
                        {
                            string instanceId = sb.ToString().ToUpperInvariant();
                            if (instanceId.StartsWith(targetPrefix))
                            {
                                matchedDevices.Add(devInfoData);
                            }
                        }
                        index++;
                        devInfoData = new SP_DEVINFO_DATA();
                        devInfoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    }

                    if (matchedDevices.Count == 0)
                    {
                        return false;
                    }

                    // 1. Disable nodes
                    foreach (var dev in matchedDevices)
                    {
                        SP_PROPCHANGE_PARAMS pcp = new SP_PROPCHANGE_PARAMS();
                        pcp.ClassInstallHeader.cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER));
                        pcp.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
                        pcp.StateChange = DICS_DISABLE;
                        pcp.Scope = DICS_FLAG_GLOBAL;
                        pcp.HwProfile = 0;

                        var devCopy = dev;
                        SetupDiSetClassInstallParams(devInfoSet, ref devCopy, ref pcp, Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS)));
                        SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devInfoSet, ref devCopy);
                    }

                    Thread.Sleep((int)(waitSeconds * 1000));

                    // 2. Enable nodes
                    foreach (var dev in matchedDevices)
                    {
                        SP_PROPCHANGE_PARAMS pcp = new SP_PROPCHANGE_PARAMS();
                        pcp.ClassInstallHeader.cbSize = (uint)Marshal.SizeOf(typeof(SP_CLASSINSTALL_HEADER));
                        pcp.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
                        pcp.StateChange = DICS_ENABLE;
                        pcp.Scope = DICS_FLAG_GLOBAL;
                        pcp.HwProfile = 0;

                        var devCopy = dev;
                        SetupDiSetClassInstallParams(devInfoSet, ref devCopy, ref pcp, Marshal.SizeOf(typeof(SP_PROPCHANGE_PARAMS)));
                        SetupDiCallClassInstaller(DIF_PROPERTYCHANGE, devInfoSet, ref devCopy);
                    }

                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfoSet);
                }
            }
        }

        #endregion

        #region Models

        public class BtDeviceItem
        {
            public string Name { get; set; }
            public string Mac { get; set; }
            public ulong MacUlong { get; set; }
            public bool Connected { get; set; }
            public bool IsHfpEnabled { get; set; }
            public BLUETOOTH_DEVICE_INFO Info;

            public override string ToString()
            {
                return string.Format("{0} ({1})", Name, Connected ? "Connected" : "Disconnected");
            }
        }

        public class ConfigData
        {
            public string DefaultMac { get; set; }
            public string DefaultName { get; set; }
            public string ReconnectMode { get; set; }
            public bool ShowNotifications { get; set; }
            public bool MinimizeToTray { get; set; }
            public string Hotkey { get; set; }
            public Dictionary<string, string> KnownDevices { get; set; }

            public ConfigData()
            {
                DefaultMac = "";
                DefaultName = "Bluetooth Headset";
                ReconnectMode = "radio";
                ShowNotifications = true;
                MinimizeToTray = true;
                Hotkey = "Ctrl+Alt+H";
                KnownDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public enum ActionType
        {
            Gui,
            Toggle,
            Enable,
            Disable,
            Status,
            List,
            Select,
            Help,
            Tray,
            RadioToggleOnly
        }

        #endregion

        #region Administrator & Registry AutoStart

        public static bool IsAdministrator()
        {
            try
            {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                {
                    return key != null && key.GetValue("EasyHFP") != null;
                }
            }
            catch { return false; }
        }

        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key != null)
                    {
                        if (enable)
                        {
                            string exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                            key.SetValue("EasyHFP", string.Format("\"{0}\" --tray", exePath));
                        }
                        else
                        {
                            key.DeleteValue("EasyHFP", false);
                        }
                    }
                }
            }
            catch { }
        }

        #endregion

        #region Config Management

        public static string GetConfigFilePath()
        {
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            return Path.Combine(appDir, "config.txt");
        }

        public static ConfigData LoadConfig()
        {
            ConfigData cfg = new ConfigData();
            string path = GetConfigFilePath();

            if (!File.Exists(path))
            {
                return cfg;
            }

            try
            {
                string[] lines = File.ReadAllLines(path);
                bool inDevicesSection = false;

                foreach (string rawLine in lines)
                {
                    string line = rawLine.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith("#") || line.StartsWith(";"))
                    {
                        continue;
                    }

                    if (line.Equals("[DEVICES]", StringComparison.OrdinalIgnoreCase))
                    {
                        inDevicesSection = true;
                        continue;
                    }

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        inDevicesSection = false;
                        continue;
                    }

                    if (inDevicesSection)
                    {
                        int eqIdx = line.IndexOf('=');
                        if (eqIdx > 0)
                        {
                            string mac = NormalizeMac(line.Substring(0, eqIdx).Trim());
                            string name = line.Substring(eqIdx + 1).Trim();
                            if (!string.IsNullOrEmpty(mac))
                            {
                                cfg.KnownDevices[mac] = name;
                            }
                        }
                    }
                    else
                    {
                        if (line.StartsWith("MAC=", StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.DefaultMac = NormalizeMac(line.Substring(4).Trim());
                        }
                        else if (line.StartsWith("NAME=", StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.DefaultName = line.Substring(5).Trim();
                        }
                        else if (line.StartsWith("RECONNECT_MODE=", StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.ReconnectMode = line.Substring(15).Trim().ToLowerInvariant();
                        }
                        else if (line.StartsWith("SHOW_NOTIFICATIONS=", StringComparison.OrdinalIgnoreCase))
                        {
                            bool show;
                            if (bool.TryParse(line.Substring(19).Trim(), out show))
                            {
                                cfg.ShowNotifications = show;
                            }
                        }
                        else if (line.StartsWith("MINIMIZE_TO_TRAY=", StringComparison.OrdinalIgnoreCase))
                        {
                            bool minTray;
                            if (bool.TryParse(line.Substring(17).Trim(), out minTray))
                            {
                                cfg.MinimizeToTray = minTray;
                            }
                        }
                        else if (line.StartsWith("HOTKEY=", StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.Hotkey = line.Substring(7).Trim();
                        }
                    }
                }

                if (!string.IsNullOrEmpty(cfg.DefaultMac) && !cfg.KnownDevices.ContainsKey(cfg.DefaultMac))
                {
                    cfg.KnownDevices[cfg.DefaultMac] = cfg.DefaultName;
                }
            }
            catch { }

            return cfg;
        }

        public static void SaveConfig(ConfigData cfg)
        {
            string path = GetConfigFilePath();
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# EasyHFP Configuration File");
                sb.AppendLine("# Generated on " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                sb.AppendLine();
                sb.AppendLine(string.Format("MAC={0}", cfg.DefaultMac));
                sb.AppendLine(string.Format("NAME={0}", cfg.DefaultName));
                sb.AppendLine(string.Format("RECONNECT_MODE={0}", string.IsNullOrEmpty(cfg.ReconnectMode) ? "radio" : cfg.ReconnectMode));
                sb.AppendLine(string.Format("SHOW_NOTIFICATIONS={0}", cfg.ShowNotifications.ToString().ToLowerInvariant()));
                sb.AppendLine(string.Format("MINIMIZE_TO_TRAY={0}", cfg.MinimizeToTray.ToString().ToLowerInvariant()));
                sb.AppendLine(string.Format("HOTKEY={0}", string.IsNullOrEmpty(cfg.Hotkey) ? "Ctrl+Alt+H" : cfg.Hotkey));
                sb.AppendLine();
                sb.AppendLine("[DEVICES]");
                foreach (var kvp in cfg.KnownDevices)
                {
                    sb.AppendLine(string.Format("{0}={1}", kvp.Key, kvp.Value));
                }

                File.WriteAllText(path, sb.ToString());
            }
            catch { }
        }

        public static string NormalizeMac(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return "";
            return mac.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
        }

        #endregion

        #region Notifications

        public static void ShowNotification(string title, string message)
        {
            try
            {
                string toastXml = string.Format(
                    "<toast><visual><binding template=\"ToastGeneric\"><text>{0}</text><text>{1}</text></binding></visual></toast>",
                    System.Security.SecurityElement.Escape(title),
                    System.Security.SecurityElement.Escape(message)
                );

                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(toastXml);

                ToastNotification toast = new ToastNotification(xmlDoc);
                ToastNotifier notifier = ToastNotificationManager.CreateToastNotifier("EasyHFP Bluetooth Manager");
                notifier.Show(toast);
                return;
            }
            catch { }

            try
            {
                using (NotifyIcon notify = new NotifyIcon())
                {
                    notify.Icon = SystemIcons.Information;
                    notify.Visible = true;
                    notify.ShowBalloonTip(3000, title, message, ToolTipIcon.Info);

                    DateTime end = DateTime.Now.AddMilliseconds(1000);
                    while (DateTime.Now < end)
                    {
                        Application.DoEvents();
                        Thread.Sleep(50);
                    }
                    notify.Visible = false;
                }
            }
            catch { }
        }

        #endregion

        #region Core Bluetooth Logic

        public static bool CheckHfpStatus(ref BLUETOOTH_DEVICE_INFO devInfo)
        {
            uint pcServices = 0;
            BluetoothEnumerateInstalledServices(IntPtr.Zero, ref devInfo, ref pcServices, null);
            if (pcServices > 0)
            {
                Guid[] services = new Guid[pcServices];
                uint res = BluetoothEnumerateInstalledServices(IntPtr.Zero, ref devInfo, ref pcServices, services);
                if (res == 0)
                {
                    for (int i = 0; i < services.Length; i++)
                    {
                        if (services[i].Equals(HfpGuid)) return true;
                    }
                }
            }
            return false;
        }

        public static List<BtDeviceItem> EnumerateBluetoothDevices()
        {
            List<BtDeviceItem> list = new List<BtDeviceItem>();

            BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams = new BLUETOOTH_DEVICE_SEARCH_PARAMS();
            searchParams.dwSize = (uint)Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS));
            searchParams.fReturnRemembered = true;
            searchParams.fReturnConnected = true;
            searchParams.hRadio = IntPtr.Zero;

            BLUETOOTH_DEVICE_INFO deviceInfo = new BLUETOOTH_DEVICE_INFO();
            deviceInfo.dwSize = (uint)Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));

            IntPtr hFind = BluetoothFindFirstDevice(ref searchParams, ref deviceInfo);

            if (hFind != IntPtr.Zero)
            {
                do
                {
                    if (deviceInfo.Address.ullLong != 0 && !string.IsNullOrEmpty(deviceInfo.szName))
                    {
                        bool hfpState = CheckHfpStatus(ref deviceInfo);
                        list.Add(new BtDeviceItem
                        {
                            Name = deviceInfo.szName,
                            Mac = deviceInfo.Address.ullLong.ToString("X12"),
                            MacUlong = deviceInfo.Address.ullLong,
                            Connected = deviceInfo.fConnected,
                            IsHfpEnabled = hfpState,
                            Info = deviceInfo
                        });
                    }
                } while (BluetoothFindNextDevice(hFind, ref deviceInfo));

                BluetoothFindDeviceClose(hFind);
            }

            return list;
        }

        public static void PerformAsymmetricReconnect(string macHex, uint targetState, string mode, bool isAdmin)
        {
            try
            {
                if (string.Equals(mode, "none", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                if (targetState == 1)
                {
                    // --- HFP ENABLING (A2DP -> HFP Call Mode) ---
                    bool nativeSuccess = NativePnpManager.RestartDeviceNodes(macHex, 2.5);
                    if (!nativeSuccess)
                    {
                        string psScript = string.Format(
                            "Get-PnpDevice | Where-Object {{ $_.InstanceId -like 'BTHENUM\\DEV_{0}*' }} | ForEach-Object {{ " +
                            "Disable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue; " +
                            "Start-Sleep -Seconds 3.0; " +
                            "Enable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }}",
                            macHex);
                        ExecutePowerShell(psScript);
                    }
                }
                else
                {
                    // --- HFP DISABLING (HFP Call Mode -> A2DP Stereo High Quality) ---
                    if (string.Equals(mode, "pnp", StringComparison.OrdinalIgnoreCase))
                    {
                        bool pnpDone = NativePnpManager.RestartDeviceNodes(macHex, 2.0);
                        if (!pnpDone)
                        {
                            string psScript = string.Format(
                                "Get-PnpDevice | Where-Object {{ $_.InstanceId -like 'BTHENUM\\DEV_{0}*' }} | ForEach-Object {{ " +
                                "Disable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue; " +
                                "Start-Sleep -Seconds 2.0; " +
                                "Enable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }}",
                                macHex);
                            ExecutePowerShell(psScript);
                        }
                    }
                    else
                    {
                        ToggleWinRtBluetoothRadio(3);
                    }
                }
            }
            catch { }
        }

        private static bool ToggleWinRtBluetoothRadio(int waitSeconds)
        {
            try
            {
                var radiosOp = Radio.GetRadiosAsync();
                var radiosTask = System.WindowsRuntimeSystemExtensions.AsTask<IReadOnlyList<Radio>>(radiosOp);
                radiosTask.Wait();
                var radios = radiosTask.Result;
                var btRadio = radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);

                if (btRadio != null)
                {
                    var offOp = btRadio.SetStateAsync(RadioState.Off);
                    var offTask = System.WindowsRuntimeSystemExtensions.AsTask<RadioAccessStatus>(offOp);
                    offTask.Wait();

                    Thread.Sleep(waitSeconds * 1000);

                    var onOp = btRadio.SetStateAsync(RadioState.On);
                    var onTask = System.WindowsRuntimeSystemExtensions.AsTask<RadioAccessStatus>(onOp);
                    onTask.Wait();

                    Thread.Sleep(2000);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static void ExecutePowerShell(string script)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", string.Format("-ExecutionPolicy Bypass -NoProfile -Command \"{0}\"", script))
                {
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                Process p = Process.Start(psi);
                if (p != null)
                {
                    p.WaitForExit(12000);
                }
            }
            catch { }
        }

        #endregion

        #region Main Entry Point

        [STAThread]
        private static void Main(string[] args)
        {
            EnableHighDpiAwareness();

            ActionType action = ActionType.Gui;
            string targetDeviceFilter = null;
            string reconnectModeOverride = null;
            bool silentMode = false;

            if (args.Length > 0)
            {
                action = ActionType.Toggle;
                for (int i = 0; i < args.Length; i++)
                {
                    string arg = args[i];

                    if (arg.Equals("--gui", StringComparison.OrdinalIgnoreCase) || arg.Equals("-g", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Gui;
                    }
                    else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase) || arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Help;
                    }
                    else if (arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) || arg.Equals("--daemon", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Tray;
                    }
                    else if (arg.Equals("--list", StringComparison.OrdinalIgnoreCase) || arg.Equals("-l", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.List;
                    }
                    else if (arg.Equals("--select", StringComparison.OrdinalIgnoreCase) || arg.Equals("--setup", StringComparison.OrdinalIgnoreCase) || arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Select;
                    }
                    else if (arg.Equals("--status", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Status;
                    }
                    else if (arg.Equals("--on", StringComparison.OrdinalIgnoreCase) || arg.Equals("--enable", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Enable;
                    }
                    else if (arg.Equals("--off", StringComparison.OrdinalIgnoreCase) || arg.Equals("--disable", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Disable;
                    }
                    else if (arg.Equals("--toggle", StringComparison.OrdinalIgnoreCase) || arg.Equals("-t", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.Toggle;
                    }
                    else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                    {
                        silentMode = true;
                    }
                    else if (arg.Equals("--radio-toggle-only", StringComparison.OrdinalIgnoreCase))
                    {
                        action = ActionType.RadioToggleOnly;
                    }
                    else if ((arg.Equals("--device", StringComparison.OrdinalIgnoreCase) || arg.Equals("-d", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                    {
                        targetDeviceFilter = args[++i];
                    }
                    else if ((arg.Equals("--reconnect-mode", StringComparison.OrdinalIgnoreCase) || arg.Equals("-m", StringComparison.OrdinalIgnoreCase)) && i + 1 < args.Length)
                    {
                        reconnectModeOverride = args[++i].ToLowerInvariant();
                    }
                }
            }

            if (action == ActionType.RadioToggleOnly)
            {
                ToggleWinRtBluetoothRadio(3);
                return;
            }

            // GUI Mode (Default)
            if (action == ActionType.Gui)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(false));
                return;
            }

            // Tray Background Mode
            if (action == ActionType.Tray)
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm(true));
                return;
            }

            // Console Actions
            AttachParentConsole();

            if (action == ActionType.Help)
            {
                ShowHelpScreen();
                return;
            }

            if (action == ActionType.List)
            {
                RunListMode();
                return;
            }

            ConfigData config = LoadConfig();

            if (action == ActionType.Select || (string.IsNullOrEmpty(config.DefaultMac) && string.IsNullOrEmpty(targetDeviceFilter)))
            {
                RunSelectMode(config);
                return;
            }

            List<BtDeviceItem> devices = EnumerateBluetoothDevices();
            BtDeviceItem targetDev = null;

            if (!string.IsNullOrEmpty(targetDeviceFilter))
            {
                string normFilterMac = NormalizeMac(targetDeviceFilter);
                targetDev = devices.FirstOrDefault(d => d.Mac.Equals(normFilterMac, StringComparison.OrdinalIgnoreCase) ||
                                                       d.Name.IndexOf(targetDeviceFilter, StringComparison.OrdinalIgnoreCase) >= 0);

                if (targetDev == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(string.Format("[ERROR] Specified device '{0}' was not found among paired Bluetooth devices!", targetDeviceFilter));
                    Console.ResetColor();
                    return;
                }
            }
            else
            {
                string targetMac = config.DefaultMac;
                targetDev = devices.FirstOrDefault(d => d.Mac.Equals(targetMac, StringComparison.OrdinalIgnoreCase));

                if (targetDev == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(string.Format("[ERROR] Saved Bluetooth headset '{0}' not found!", config.DefaultName));
                    Console.ResetColor();
                    RunSelectMode(config);
                    return;
                }
            }

            BLUETOOTH_DEVICE_INFO devInfo = targetDev.Info;
            bool isHfpActive = CheckHfpStatus(ref devInfo);
            targetDev.IsHfpEnabled = isHfpActive;

            if (action == ActionType.Status)
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=================================================");
                Console.WriteLine("          EasyHFP Device Status Report           ");
                Console.WriteLine("=================================================");
                Console.ResetColor();
                Console.WriteLine(string.Format("Device Name : {0}", targetDev.Name));
                Console.WriteLine(string.Format("MAC Address : {0}", targetDev.Mac));
                Console.WriteLine(string.Format("Link Status : {0}", targetDev.Connected ? "Connected" : "Disconnected"));
                Console.Write("Audio Mode  : ");
                if (isHfpActive)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("ENABLED (Hands-Free Telephony / Call Mode)");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("DISABLED (High-Quality A2DP Stereo Mode)");
                }
                Console.ResetColor();
                Console.WriteLine("=================================================\n");
                return;
            }

            uint targetState = 0;
            if (action == ActionType.Enable)
            {
                if (isHfpActive)
                {
                    Console.WriteLine(string.Format("[INFO] Hands-Free Telephony is ALREADY ENABLED for {0}.", targetDev.Name));
                    return;
                }
                targetState = 1;
            }
            else if (action == ActionType.Disable)
            {
                if (!isHfpActive)
                {
                    Console.WriteLine(string.Format("[INFO] Hands-Free Telephony is ALREADY DISABLED for {0} (Stereo Active).", targetDev.Name));
                    return;
                }
                targetState = 0;
            }
            else
            {
                targetState = isHfpActive ? 0u : 1u;
            }

            string modeStr = targetState == 0 ? "DISABLED (Stereo)" : "ENABLED (Call Mode)";
            Console.WriteLine(string.Format("[INFO] Switching {0} to: {1}", targetDev.Name, modeStr));

            uint res = BluetoothSetServiceState(IntPtr.Zero, ref devInfo, ref HfpGuid, targetState);
            if (res == 0)
            {
                string rMode = !string.IsNullOrEmpty(reconnectModeOverride) ? reconnectModeOverride : config.ReconnectMode;
                PerformAsymmetricReconnect(targetDev.Mac, targetState, rMode, IsAdministrator());

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("[SUCCESS] Switched {0} successfully!", targetDev.Name));
                Console.ResetColor();

                if (config.ShowNotifications)
                {
                    string title = targetState == 0 ? "Hands-Free: DISABLED (Stereo Active)" : "Hands-Free: ENABLED (Call Mode Active)";
                    string body = string.Format("Switched {0} successfully.", targetDev.Name);
                    ShowNotification(title, body);
                }

                if (!silentMode)
                {
                    Thread.Sleep(1500);
                }
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format("[ERROR] BluetoothSetServiceState failed: {0}", res));
                Console.ResetColor();
            }
        }

        private static void RunListMode()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========================================================================");
            Console.WriteLine("               EasyHFP - Paired Bluetooth Audio Devices                  ");
            Console.WriteLine("=========================================================================");
            Console.ResetColor();

            List<BtDeviceItem> devices = EnumerateBluetoothDevices();
            if (devices.Count == 0)
            {
                Console.WriteLine("No paired Bluetooth audio devices found.");
                return;
            }

            ConfigData cfg = LoadConfig();
            for (int i = 0; i < devices.Count; i++)
            {
                BtDeviceItem dev = devices[i];
                bool isDef = dev.Mac.Equals(cfg.DefaultMac, StringComparison.OrdinalIgnoreCase);

                Console.ForegroundColor = isDef ? ConsoleColor.Yellow : ConsoleColor.White;
                Console.Write(string.Format("[{0}] {1} ", i + 1, dev.Name));
                if (isDef) Console.Write("(Default) ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(string.Format("[MAC: {0}] ", dev.Mac));
                Console.ForegroundColor = dev.Connected ? ConsoleColor.Green : ConsoleColor.DarkGray;
                Console.Write(string.Format("[{0}] ", dev.Connected ? "Connected" : "Disconnected"));
                Console.ForegroundColor = dev.IsHfpEnabled ? ConsoleColor.Yellow : ConsoleColor.Cyan;
                Console.WriteLine(dev.IsHfpEnabled ? "[HFP: ENABLED (Call Mode)]" : "[HFP: DISABLED (Stereo)]");
                Console.ResetColor();
            }
            Console.WriteLine("=========================================================================\n");
        }

        private static void RunSelectMode(ConfigData config)
        {
            List<BtDeviceItem> devices = EnumerateBluetoothDevices();
            if (devices.Count == 0)
            {
                Console.WriteLine("No paired devices found.");
                return;
            }

            Console.WriteLine("\nSelect Default Headset:");
            for (int i = 0; i < devices.Count; i++)
            {
                Console.WriteLine(string.Format(" [{0}] {1} (MAC: {2})", i + 1, devices[i].Name, devices[i].Mac));
            }

            int choice = 0;
            while (choice < 1 || choice > devices.Count)
            {
                Console.Write(string.Format("Enter choice (1-{0}): ", devices.Count));
                string input = Console.ReadLine();
                int.TryParse(input, out choice);
            }

            BtDeviceItem sel = devices[choice - 1];
            config.DefaultMac = sel.Mac;
            config.DefaultName = sel.Name;
            config.KnownDevices[sel.Mac] = sel.Name;
            SaveConfig(config);
            Console.WriteLine(string.Format("[SUCCESS] Default set to: {0}\n", sel.Name));
        }

        private static void ShowHelpScreen()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========================================================================");
            Console.WriteLine("              EasyHFP - Windows Bluetooth Audio Manager                  ");
            Console.WriteLine("=========================================================================");
            Console.ResetColor();
            Console.WriteLine("Usage: EasyHFP.exe [ACTION] [OPTIONS]\n");
            Console.WriteLine("ACTIONS:");
            Console.WriteLine("  (no args)               Launch Modern GUI Control Window");
            Console.WriteLine("  --tray, --daemon        Run as a resident System Tray background service");
            Console.WriteLine("  --toggle, -t            Toggle HFP state (Stereo <-> Call Mode)");
            Console.WriteLine("  --on, --enable          Enable Hands-Free Telephony (Microphone / Call Mode)");
            Console.WriteLine("  --off, --disable        Disable Hands-Free Telephony (High Quality A2DP Stereo)");
            Console.WriteLine("  --status                Query & display current HFP profile state");
            Console.WriteLine("  --list, -l              List all paired Bluetooth audio devices");
            Console.WriteLine("  --select, -s            Launch interactive setup wizard");
            Console.WriteLine("  --help, -h              Display this documentation\n");
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  --device, -d <MAC|NAME> Target a specific headset instead of default config");
            Console.WriteLine("  --reconnect-mode, -m    Reconnect strategy: 'radio' (default), 'pnp', 'none'");
            Console.WriteLine("  --silent                Suppress exit pause\n");
            Console.WriteLine("=========================================================================\n");
        }

        #endregion

        #region Hotkey Hook

        public class HotkeyWindow : NativeWindow, IDisposable
        {
            public const int WM_HOTKEY = 0x0312;
            public const int HOTKEY_ID = 9001;

            public const uint MOD_ALT = 0x0001;
            public const uint MOD_CONTROL = 0x0002;
            public const uint MOD_NOREPEAT = 0x4000;

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

            public event Action HotkeyPressed;

            public HotkeyWindow(uint modifiers, Keys key)
            {
                CreateHandle(new CreateParams());
                RegisterHotKey(Handle, HOTKEY_ID, modifiers, (uint)key);
            }

            protected override void WndProc(ref Message m)
            {
                if (m.Msg == WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
                {
                    if (HotkeyPressed != null) HotkeyPressed();
                }
                base.WndProc(ref m);
            }

            public void Dispose()
            {
                UnregisterHotKey(Handle, HOTKEY_ID);
                DestroyHandle();
            }
        }

        #endregion

        #region Modern Graphical User Interface (High DPI Aware, Crisp 2K Rendering)

        public class MainForm : Form
        {
            private ConfigData config;
            private List<BtDeviceItem> currentDevices;
            private BtDeviceItem selectedDevice;
            private bool isBusy = false;
            private Image customLogoImage;

            private NotifyIcon trayIcon;
            private ContextMenuStrip trayMenu;
            private HotkeyWindow hotkeyWindow;
            private System.Windows.Forms.Timer refreshTimer;

            // UI Elements
            private Panel headerPanel;
            private PictureBox logoPictureBox;
            private Label titleLabel;
            private Label subTitleLabel;
            private Button refreshButton;

            private Panel heroCard;
            private Label deviceNameLabel;
            private Label deviceMacLabel;
            private Label statusBadge;
            private Button toggleButton;

            private Panel settingsCard;
            private Label deviceSelectLabel;
            private ComboBox deviceComboBox;
            private Label reconnectLabel;
            private ComboBox reconnectComboBox;
            private CheckBox autoStartCheck;
            private CheckBox toastCheck;
            private CheckBox minToTrayCheck;

            private Label hotkeyLabel;
            private Button trayButton;

            public MainForm(bool startInTray)
            {
                config = LoadConfig();
                currentDevices = new List<BtDeviceItem>();
                customLogoImage = LoadCustomLogo();

                InitializeComponent();
                InitializeTray();
                InitializeHotkey();

                refreshTimer = new System.Windows.Forms.Timer();
                refreshTimer.Interval = 3500;
                refreshTimer.Tick += (s, e) => ReloadDeviceState(false);
                refreshTimer.Start();

                ReloadDeviceState(true);

                if (startInTray)
                {
                    WindowState = FormWindowState.Minimized;
                    ShowInTaskbar = false;
                    Visible = false;
                }
            }

            private Image LoadCustomLogo()
            {
                string appDir = AppDomain.CurrentDomain.BaseDirectory;
                string[] candidates = new string[] {
                    Path.Combine(appDir, "easy_hfp.jpeg"),
                    Path.Combine(appDir, "easy_hfp.jpg"),
                    Path.Combine(appDir, "easy_hfp.png"),
                    Path.Combine(appDir, "..", "easy_hfp.jpeg")
                };

                foreach (string path in candidates)
                {
                    if (File.Exists(path))
                    {
                        try
                        {
                            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                            using (Image img = Image.FromStream(fs))
                            {
                                return new Bitmap(img);
                            }
                        }
                        catch { }
                    }
                }
                return null;
            }

            private void InitializeComponent()
            {
                SuspendLayout();

                AutoScaleMode = AutoScaleMode.Dpi;
                Text = "EasyHFP - Bluetooth Audio Manager";
                Size = new Size(500, 670);
                MinimumSize = new Size(500, 670);
                MaximumSize = new Size(500, 670);
                FormBorderStyle = FormBorderStyle.FixedSingle;
                MaximizeBox = false;
                StartPosition = FormStartPosition.CenterScreen;
                BackColor = Color.FromArgb(16, 16, 20);
                ForeColor = Color.White;
                Font = new Font("Segoe UI", 10f, FontStyle.Regular, GraphicsUnit.Point);

                if (customLogoImage != null)
                {
                    try
                    {
                        using (Bitmap iconBmp = new Bitmap(customLogoImage, 64, 64))
                        {
                            Icon = Icon.FromHandle(iconBmp.GetHicon());
                        }
                    }
                    catch { }
                }

                // --- HEADER PANEL ---
                headerPanel = new Panel
                {
                    Dock = DockStyle.Top,
                    Height = 72,
                    BackColor = Color.FromArgb(22, 22, 28),
                    Padding = new Padding(16, 12, 16, 12)
                };

                int textStartX = 18;
                if (customLogoImage != null)
                {
                    logoPictureBox = new PictureBox
                    {
                        Size = new Size(46, 46),
                        Location = new Point(14, 13),
                        SizeMode = PictureBoxSizeMode.Zoom,
                        Image = customLogoImage,
                        BackColor = Color.Transparent
                    };
                    headerPanel.Controls.Add(logoPictureBox);
                    textStartX = 68;
                }

                titleLabel = new Label
                {
                    Text = "EasyHFP",
                    Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(textStartX, 10),
                    AutoSize = true
                };

                subTitleLabel = new Label
                {
                    Text = "Windows Bluetooth Audio Manager",
                    Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(140, 140, 160),
                    Location = new Point(textStartX + 2, 40),
                    AutoSize = true
                };

                refreshButton = new Button
                {
                    Text = "Refresh",
                    Size = new Size(90, 34),
                    Location = new Point(370, 19),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(32, 32, 42),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                refreshButton.FlatAppearance.BorderSize = 0;
                refreshButton.Click += (s, e) => ReloadDeviceState(false);

                headerPanel.Controls.Add(titleLabel);
                headerPanel.Controls.Add(subTitleLabel);
                headerPanel.Controls.Add(refreshButton);

                // --- HERO STATUS CARD ---
                heroCard = new Panel
                {
                    Location = new Point(16, 88),
                    Size = new Size(452, 225),
                    BackColor = Color.FromArgb(24, 24, 32)
                };
                heroCard.Paint += (s, e) => DrawRoundedBorder(e.Graphics, heroCard, 12, Color.FromArgb(40, 40, 56));

                deviceNameLabel = new Label
                {
                    Text = "Scanning headset...",
                    Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                    ForeColor = Color.White,
                    Location = new Point(18, 16),
                    Size = new Size(415, 28)
                };

                deviceMacLabel = new Label
                {
                    Text = "MAC: --:--:--:--:--:-- • Status: Checking",
                    Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(140, 140, 165),
                    Location = new Point(19, 46),
                    AutoSize = true
                };

                statusBadge = new Label
                {
                    Text = "CHECKING STATUS...",
                    Font = new Font("Segoe UI", 10f, FontStyle.Bold),
                    ForeColor = Color.White,
                    BackColor = Color.FromArgb(40, 40, 56),
                    Location = new Point(18, 78),
                    Size = new Size(416, 38),
                    TextAlign = ContentAlignment.MiddleCenter
                };

                toggleButton = new Button
                {
                    Text = "Switch Audio Profile",
                    Font = new Font("Segoe UI", 11.5f, FontStyle.Bold),
                    Size = new Size(416, 54),
                    Location = new Point(18, 128),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(99, 102, 241),
                    ForeColor = Color.White,
                    Cursor = Cursors.Hand
                };
                toggleButton.FlatAppearance.BorderSize = 0;
                toggleButton.Click += (s, e) => TriggerToggle();

                heroCard.Controls.Add(deviceNameLabel);
                heroCard.Controls.Add(deviceMacLabel);
                heroCard.Controls.Add(statusBadge);
                heroCard.Controls.Add(toggleButton);

                // --- SETTINGS CARD ---
                settingsCard = new Panel
                {
                    Location = new Point(16, 325),
                    Size = new Size(452, 235),
                    BackColor = Color.FromArgb(24, 24, 32)
                };
                settingsCard.Paint += (s, e) => DrawRoundedBorder(e.Graphics, settingsCard, 12, Color.FromArgb(40, 40, 56));

                deviceSelectLabel = new Label
                {
                    Text = "Active Headset Profile:",
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(160, 160, 185),
                    Location = new Point(18, 14),
                    AutoSize = true
                };

                deviceComboBox = new ComboBox
                {
                    Location = new Point(18, 36),
                    Size = new Size(416, 30),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = Color.FromArgb(16, 16, 22),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9.5f)
                };
                deviceComboBox.SelectedIndexChanged += DeviceComboBox_SelectedIndexChanged;

                reconnectLabel = new Label
                {
                    Text = "Reconnection Strategy:",
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    ForeColor = Color.FromArgb(160, 160, 185),
                    Location = new Point(18, 74),
                    AutoSize = true
                };

                reconnectComboBox = new ComboBox
                {
                    Location = new Point(18, 96),
                    Size = new Size(416, 30),
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    BackColor = Color.FromArgb(16, 16, 22),
                    ForeColor = Color.White,
                    FlatStyle = FlatStyle.Flat,
                    Font = new Font("Segoe UI", 9.5f)
                };
                reconnectComboBox.Items.AddRange(new object[] {
                    "Master Bluetooth Radio Soft-Toggle (Recommended)",
                    "Target Headset PnP Node Reset",
                    "None (Win32 Service Only)"
                });
                reconnectComboBox.SelectedIndex = config.ReconnectMode == "pnp" ? 1 : (config.ReconnectMode == "none" ? 2 : 0);
                reconnectComboBox.SelectedIndexChanged += (s, e) =>
                {
                    config.ReconnectMode = reconnectComboBox.SelectedIndex == 1 ? "pnp" : (reconnectComboBox.SelectedIndex == 2 ? "none" : "radio");
                    SaveConfig(config);
                };

                toastCheck = new CheckBox
                {
                    Text = "Show Windows Toast notifications on switch",
                    Location = new Point(18, 136),
                    Size = new Size(416, 24),
                    Checked = config.ShowNotifications,
                    ForeColor = Color.FromArgb(200, 200, 220),
                    Font = new Font("Segoe UI", 9f)
                };
                toastCheck.CheckedChanged += (s, e) => { config.ShowNotifications = toastCheck.Checked; SaveConfig(config); };

                autoStartCheck = new CheckBox
                {
                    Text = "Start EasyHFP with Windows (in System Tray)",
                    Location = new Point(18, 164),
                    Size = new Size(416, 24),
                    Checked = IsAutoStartEnabled(),
                    ForeColor = Color.FromArgb(200, 200, 220),
                    Font = new Font("Segoe UI", 9f)
                };
                autoStartCheck.CheckedChanged += (s, e) => SetAutoStart(autoStartCheck.Checked);

                minToTrayCheck = new CheckBox
                {
                    Text = "Minimize to System Tray on close (keep hotkey active)",
                    Location = new Point(18, 192),
                    Size = new Size(416, 24),
                    Checked = config.MinimizeToTray,
                    ForeColor = Color.FromArgb(200, 200, 220),
                    Font = new Font("Segoe UI", 9f)
                };
                minToTrayCheck.CheckedChanged += (s, e) => { config.MinimizeToTray = minToTrayCheck.Checked; SaveConfig(config); };

                settingsCard.Controls.Add(deviceSelectLabel);
                settingsCard.Controls.Add(deviceComboBox);
                settingsCard.Controls.Add(reconnectLabel);
                settingsCard.Controls.Add(reconnectComboBox);
                settingsCard.Controls.Add(toastCheck);
                settingsCard.Controls.Add(autoStartCheck);
                settingsCard.Controls.Add(minToTrayCheck);

                // --- FOOTER SECTION ---
                hotkeyLabel = new Label
                {
                    Text = "Global Hotkey: Ctrl + Alt + H",
                    Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                    ForeColor = Color.FromArgb(130, 130, 160),
                    Location = new Point(18, 578),
                    AutoSize = true
                };

                trayButton = new Button
                {
                    Text = "Minimize to Tray",
                    Size = new Size(150, 36),
                    Location = new Point(318, 570),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Color.FromArgb(32, 32, 42),
                    ForeColor = Color.White,
                    Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                    Cursor = Cursors.Hand
                };
                trayButton.FlatAppearance.BorderSize = 0;
                trayButton.Click += (s, e) => HideToTray();

                Controls.Add(headerPanel);
                Controls.Add(heroCard);
                Controls.Add(settingsCard);
                Controls.Add(hotkeyLabel);
                Controls.Add(trayButton);

                FormClosing += MainForm_FormClosing;
                ResumeLayout(false);
            }

            private void DrawRoundedBorder(Graphics g, Panel panel, int radius, Color borderColor)
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                using (Pen pen = new Pen(borderColor, 1.5f))
                {
                    Rectangle rect = new Rectangle(0, 0, panel.Width - 1, panel.Height - 1);
                    using (GraphicsPath path = GetRoundedPath(rect, radius))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            private GraphicsPath GetRoundedPath(Rectangle rect, int radius)
            {
                GraphicsPath path = new GraphicsPath();
                float d = radius * 2f;
                path.AddArc(rect.X, rect.Y, d, d, 180, 90);
                path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
                path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
                path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }

            private void InitializeTray()
            {
                trayMenu = new ContextMenuStrip();
                var toggleItem = new ToolStripMenuItem("Toggle Profile (Ctrl+Alt+H)", null, (s, e) => TriggerToggle())
                { Font = new Font(Font, FontStyle.Bold) };
                var openItem = new ToolStripMenuItem("Open EasyHFP Window", null, (s, e) => RestoreFromTray());
                var exitItem = new ToolStripMenuItem("Exit", null, (s, e) => { trayIcon.Visible = false; Application.Exit(); });

                trayMenu.Items.Add(toggleItem);
                trayMenu.Items.Add(openItem);
                trayMenu.Items.Add(new ToolStripSeparator());
                trayMenu.Items.Add(exitItem);

                trayIcon = new NotifyIcon
                {
                    Text = "EasyHFP Bluetooth Manager",
                    ContextMenuStrip = trayMenu,
                    Visible = true
                };

                trayIcon.DoubleClick += (s, e) => RestoreFromTray();
                trayIcon.MouseClick += (s, e) =>
                {
                    if (e.Button == MouseButtons.Left)
                    {
                        TriggerToggle();
                    }
                };
            }

            private void InitializeHotkey()
            {
                try
                {
                    uint modifiers = HotkeyWindow.MOD_CONTROL | HotkeyWindow.MOD_ALT | HotkeyWindow.MOD_NOREPEAT;
                    hotkeyWindow = new HotkeyWindow(modifiers, Keys.H);
                    hotkeyWindow.HotkeyPressed += () => TriggerToggle();
                }
                catch { }
            }

            private void ReloadDeviceState(bool initialLoad)
            {
                if (isBusy) return;

                new Thread(() =>
                {
                    config = LoadConfig();
                    var list = EnumerateBluetoothDevices();

                    BeginInvoke(new Action(() =>
                    {
                        currentDevices = list;
                        UpdateDeviceListUI();
                        UpdateStatusHeroUI();
                    }));
                }).Start();
            }

            private void UpdateDeviceListUI()
            {
                deviceComboBox.SelectedIndexChanged -= DeviceComboBox_SelectedIndexChanged;
                deviceComboBox.Items.Clear();

                int selIdx = -1;
                for (int i = 0; i < currentDevices.Count; i++)
                {
                    var dev = currentDevices[i];
                    deviceComboBox.Items.Add(dev);
                    if (dev.Mac.Equals(config.DefaultMac, StringComparison.OrdinalIgnoreCase))
                    {
                        selIdx = i;
                        selectedDevice = dev;
                    }
                }

                if (selIdx >= 0)
                {
                    deviceComboBox.SelectedIndex = selIdx;
                }
                else if (currentDevices.Count > 0)
                {
                    deviceComboBox.SelectedIndex = 0;
                    selectedDevice = currentDevices[0];
                }

                deviceComboBox.SelectedIndexChanged += DeviceComboBox_SelectedIndexChanged;
            }

            private void DeviceComboBox_SelectedIndexChanged(object sender, EventArgs e)
            {
                BtDeviceItem dev = deviceComboBox.SelectedItem as BtDeviceItem;
                if (dev != null)
                {
                    selectedDevice = dev;
                    config.DefaultMac = dev.Mac;
                    config.DefaultName = dev.Name;
                    config.KnownDevices[dev.Mac] = dev.Name;
                    SaveConfig(config);
                    UpdateStatusHeroUI();
                }
            }

            private void UpdateStatusHeroUI()
            {
                if (selectedDevice != null)
                {
                    deviceNameLabel.Text = selectedDevice.Name;
                    string linkStatus = selectedDevice.Connected ? "Connected" : "Disconnected";
                    deviceMacLabel.Text = string.Format("MAC: {0} • Link: {1}", selectedDevice.Mac, linkStatus);

                    BLUETOOTH_DEVICE_INFO info = selectedDevice.Info;
                    bool isHfp = CheckHfpStatus(ref info);
                    selectedDevice.IsHfpEnabled = isHfp;

                    if (isHfp)
                    {
                        statusBadge.Text = "HANDS-FREE CALL MODE ACTIVE (HFP)";
                        statusBadge.BackColor = Color.FromArgb(217, 119, 6);
                        statusBadge.ForeColor = Color.White;

                        toggleButton.Text = "Switch to Stereo Mode (A2DP)";
                        toggleButton.BackColor = Color.FromArgb(16, 185, 129);
                    }
                    else
                    {
                        statusBadge.Text = "HIGH-QUALITY STEREO ACTIVE (A2DP)";
                        statusBadge.BackColor = Color.FromArgb(5, 150, 105);
                        statusBadge.ForeColor = Color.White;

                        toggleButton.Text = "Switch to Call Mode (HFP Microphone)";
                        toggleButton.BackColor = Color.FromArgb(99, 102, 241);
                    }

                    string shortName = selectedDevice.Name.Length > 20 ? selectedDevice.Name.Substring(0, 17) + "..." : selectedDevice.Name;
                    trayIcon.Text = string.Format("EasyHFP: {0}\n[{1}]", shortName, isHfp ? "Call Mode" : "Stereo Mode");
                    trayIcon.Icon = GenerateTrayIcon(isHfp, selectedDevice.Connected);
                }
                else
                {
                    deviceNameLabel.Text = "No Bluetooth Headset Found";
                    deviceMacLabel.Text = "Please pair a device in Windows Settings";
                    statusBadge.Text = "NO DEVICE SELECTED";
                    statusBadge.BackColor = Color.FromArgb(40, 40, 56);
                    toggleButton.Text = "Pair a Device in Windows Settings";
                    toggleButton.BackColor = Color.FromArgb(40, 40, 56);
                }
            }

            public void TriggerToggle()
            {
                if (isBusy || selectedDevice == null) return;
                isBusy = true;

                toggleButton.Text = "Switching Profile... Please wait";
                toggleButton.Enabled = false;
                statusBadge.Text = "SWITCHING IN PROGRESS...";
                statusBadge.BackColor = Color.FromArgb(70, 70, 90);

                new Thread(() =>
                {
                    try
                    {
                        BLUETOOTH_DEVICE_INFO devInfo = selectedDevice.Info;
                        bool isHfp = CheckHfpStatus(ref devInfo);
                        uint targetState = isHfp ? 0u : 1u;

                        uint result = BluetoothSetServiceState(IntPtr.Zero, ref devInfo, ref HfpGuid, targetState);
                        if (result == 0)
                        {
                            PerformAsymmetricReconnect(selectedDevice.Mac, targetState, config.ReconnectMode, IsAdministrator());

                            string title = targetState == 0 ? "Hands-Free: Disabled (Stereo Active)" : "Hands-Free: Enabled (Call Mode Active)";
                            string body = targetState == 0
                                ? string.Format("High quality stereo audio active for {0}!", selectedDevice.Name)
                                : string.Format("Microphone & hands-free call mode active for {0}!", selectedDevice.Name);

                            if (config.ShowNotifications)
                            {
                                ShowNotification(title, body);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        isBusy = false;
                        Thread.Sleep(1000);
                        BeginInvoke(new Action(() =>
                        {
                            toggleButton.Enabled = true;
                            ReloadDeviceState(false);
                        }));
                    }
                }).Start();
            }

            private Icon GenerateTrayIcon(bool isHfpActive, bool isConnected)
            {
                try
                {
                    using (Bitmap bmp = new Bitmap(32, 32))
                    {
                        using (Graphics g = Graphics.FromImage(bmp))
                        {
                            g.SmoothingMode = SmoothingMode.HighQuality;
                            g.Clear(Color.Transparent);

                            Color bgColor = !isConnected ? Color.Gray : (isHfpActive ? Color.FromArgb(245, 158, 11) : Color.FromArgb(16, 185, 129));
                            using (Brush bgBrush = new SolidBrush(bgColor))
                            {
                                g.FillEllipse(bgBrush, 2, 2, 28, 28);
                            }

                            using (Pen pen = new Pen(Color.FromArgb(20, 20, 25), 2))
                            {
                                g.DrawEllipse(pen, 2, 2, 28, 28);
                            }

                            string glyph = isHfpActive ? "M" : "S";
                            using (Font font = new Font("Segoe UI", 13, FontStyle.Bold, GraphicsUnit.Pixel))
                            {
                                SizeF size = g.MeasureString(glyph, font);
                                float x = (32 - size.Width) / 2f;
                                float y = (32 - size.Height) / 2f;
                                using (Brush textBrush = new SolidBrush(Color.White))
                                {
                                    g.DrawString(glyph, font, textBrush, x, y);
                                }
                            }
                        }
                        IntPtr hIcon = bmp.GetHicon();
                        return Icon.FromHandle(hIcon);
                    }
                }
                catch
                {
                    return SystemIcons.Application;
                }
            }

            private void HideToTray()
            {
                Hide();
                if (config.ShowNotifications)
                {
                    trayIcon.ShowBalloonTip(2000, "EasyHFP Active in Tray", "EasyHFP is running in the background. Press Ctrl+Alt+H to toggle anytime.", ToolTipIcon.Info);
                }
            }

            private void RestoreFromTray()
            {
                Show();
                WindowState = FormWindowState.Normal;
                BringToFront();
                ReloadDeviceState(false);
            }

            private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
            {
                if (config.MinimizeToTray && e.CloseReason == CloseReason.UserClosing)
                {
                    e.Cancel = true;
                    HideToTray();
                }
                else
                {
                    if (hotkeyWindow != null) hotkeyWindow.Dispose();
                    if (trayIcon != null) { trayIcon.Visible = false; trayIcon.Dispose(); }
                    if (refreshTimer != null) refreshTimer.Dispose();
                }
            }
        }

        #endregion
    }
}
