using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Windows.Devices.Radios;

namespace EasyHFP
{
    internal class Program
    {
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

        private static Guid HfpGuid = new Guid("0000111e-0000-1000-8000-00805f9b34fb");

        public class BtDeviceItem
        {
            public string Name { get; set; }
            public string Mac { get; set; }
            public ulong MacUlong { get; set; }
            public bool Connected { get; set; }
            public BLUETOOTH_DEVICE_INFO Info;
        }

        #region Console Logging Helpers

        private static void LogInfo(string message)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(string.Format("[{0}] ", ts));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write("[INFO] ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void LogReconnect(string message)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(string.Format("[{0}] ", ts));
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write("[RECONNECT] ");
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void LogSuccess(string message)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(string.Format("[{0}] ", ts));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[SUCCESS] ");
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        private static void LogError(string message)
        {
            string ts = DateTime.Now.ToString("HH:mm:ss");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(string.Format("[{0}] ", ts));
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write("[ERROR] ");
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(message);
            Console.ResetColor();
        }

        #endregion

        [STAThread]
        private static void Main(string[] args)
        {
            Console.Title = "EasyHFP - Bluetooth Audio Manager";

            // Support standalone radio toggle helper flag
            if (args.Any(a => a.Equals("--radio-toggle-only", StringComparison.OrdinalIgnoreCase)))
            {
                ToggleWinRtBluetoothRadio(3);
                return;
            }

            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string configFile = Path.Combine(appDir, "config.txt");

            bool forceSetup = args.Any(a => a.Equals("--setup", StringComparison.OrdinalIgnoreCase) || a.Equals("-s", StringComparison.OrdinalIgnoreCase));

            if (forceSetup && File.Exists(configFile))
            {
                try { File.Delete(configFile); } catch { }
                LogInfo("Existing config.txt removed. Launching setup mode...\n");
            }

            // --- SETUP MODE ---
            if (!File.Exists(configFile))
            {
                RunSetupMode(configFile);
                return;
            }

            // --- SILENT TOGGLE MODE ---
            LogInfo("Loading configuration file...");
            string targetMac = "";
            string targetName = "Bluetooth Headset";

            try
            {
                string[] lines = File.ReadAllLines(configFile);
                foreach (string line in lines)
                {
                    if (line.StartsWith("MAC=", StringComparison.OrdinalIgnoreCase))
                    {
                        targetMac = line.Substring(4).Trim().Replace(":", "").Replace("-", "");
                    }
                    else if (line.StartsWith("NAME=", StringComparison.OrdinalIgnoreCase))
                    {
                        targetName = line.Substring(5).Trim();
                    }
                }
            }
            catch { }

            if (string.IsNullOrEmpty(targetMac))
            {
                LogError("No valid MAC address found in config.txt. Launching setup mode...");
                try { File.Delete(configFile); } catch { }
                RunSetupMode(configFile);
                return;
            }

            LogInfo(string.Format("Target headset: {0} (MAC: {1})", targetName, targetMac));
            LogInfo("Scanning Bluetooth devices...");

            List<BtDeviceItem> devices = EnumerateBluetoothDevices();
            BtDeviceItem targetDev = devices.FirstOrDefault(d => d.Mac.Equals(targetMac, StringComparison.OrdinalIgnoreCase));

            if (targetDev == null)
            {
                string errMsg = string.Format("Saved Bluetooth headset not found! MAC: '{0}'", targetMac);
                LogError(errMsg);
                ShowNotification("EasyHFP Error", errMsg);
                Thread.Sleep(2000);
                return;
            }

            BLUETOOTH_DEVICE_INFO devInfo = targetDev.Info;

            LogInfo("Checking Hands-Free Telephony service status...");
            uint pcServices = 0;
            BluetoothEnumerateInstalledServices(IntPtr.Zero, ref devInfo, ref pcServices, null);
            Guid[] services = new Guid[pcServices];
            if (pcServices > 0)
            {
                BluetoothEnumerateInstalledServices(IntPtr.Zero, ref devInfo, ref pcServices, services);
            }

            bool isHfpActive = services.Contains(HfpGuid);
            uint targetState = isHfpActive ? 0u : 1u;

            string targetModeStr = targetState == 0 ? "DISABLED (A2DP Stereo Mode)" : "ENABLED (Microphone & Call Mode)";
            LogInfo(string.Format("Current HFP Status: {0} -> Switching to: {1}", isHfpActive ? "ENABLED" : "DISABLED", targetModeStr));

            LogInfo("Toggling Hands-Free service state via Win32 API...");
            uint result = BluetoothSetServiceState(IntPtr.Zero, ref devInfo, ref HfpGuid, targetState);

            if (result == 0)
            {
                // Asymmetric Reconnect Execution
                PerformAsymmetricReconnect(targetMac, targetState);

                string successMsg = targetState == 0
                    ? string.Format("Hands-Free Telephony DISABLED for {0} (A2DP Stereo Active)!", targetName)
                    : string.Format("Hands-Free Telephony ENABLED for {0} (Call Mode Active)!", targetName);

                LogSuccess(successMsg);

                string title = targetState == 0 ? "Hands-Free Telephony: DISABLED 🎧" : "Hands-Free Telephony: ENABLED 🎙️";
                string body = targetState == 0
                    ? string.Format("Hands-Free Telephony disabled for {0}. A2DP high-quality stereo audio active!", targetName)
                    : string.Format("Hands-Free Telephony enabled for {0}. Microphone & hands-free call mode active!", targetName);

                ShowNotification(title, body);

                LogInfo("Closing window in 2 seconds...");
                Thread.Sleep(2000);
            }
            else
            {
                string errMsg = string.Format("BluetoothSetServiceState failed with error code: {0}", result);
                LogError(errMsg);
                ShowNotification("EasyHFP Error", errMsg);
                Thread.Sleep(2000);
            }
        }

        private static void PerformAsymmetricReconnect(string macHex, uint targetState)
        {
            try
            {
                if (targetState == 1)
                {
                    // --- HFP ENABLING (A2DP -> HFP Call Mode) ---
                    // Untouched - Working perfectly
                    LogReconnect("Initiating PnP reconnect for headset (HFP Call Mode)...");
                    LogReconnect("Disabling headset PnP node...");
                    LogReconnect("Waiting 3.5s for handles to reset...");

                    string psScript = string.Format(
                        "Get-PnpDevice | Where-Object {{ $_.InstanceId -like 'BTHENUM\\DEV_{0}*' }} | ForEach-Object {{ " +
                        "Disable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue; " +
                        "Start-Sleep -Seconds 3.5; " +
                        "Enable-PnpDevice -InstanceId $_.InstanceId -Confirm:$false -ErrorAction SilentlyContinue }}",
                        macHex);

                    ExecutePowerShell(psScript);
                    LogReconnect("Re-enabled headset PnP node.");
                }
                else
                {
                    // --- HFP DISABLING (HFP Call Mode -> A2DP Stereo High Quality) ---
                    // Graceful WinRT Master Bluetooth Radio Soft Toggle (Action Center Bluetooth Button)
                    bool winrtSuccess = ToggleWinRtBluetoothRadio(3);

                    if (!winrtSuccess)
                    {
                        LogError("WinRT Radio toggle failed or unavailable. Falling back to PowerShell WinRT Radio handler...");
                        string appPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                        ExecutePowerShell(string.Format("& '{0}' --radio-toggle-only", appPath));
                    }
                }
            }
            catch (Exception ex)
            {
                LogError("Asymmetric reconnect error: " + ex.Message);
            }
        }

        private static bool ToggleWinRtBluetoothRadio(int waitSeconds)
        {
            try
            {
                LogReconnect("Toggling Windows Master Bluetooth Radio OFF (Soft Disconnect via WinRT API)...");
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

                    LogReconnect(string.Format("Waiting {0}.0s for Bluetooth Radio to gracefully close L2CAP audio channels...", waitSeconds));
                    Thread.Sleep(waitSeconds * 1000);

                    LogReconnect("Toggling Windows Master Bluetooth Radio ON...");
                    var onOp = btRadio.SetStateAsync(RadioState.On);
                    var onTask = System.WindowsRuntimeSystemExtensions.AsTask<RadioAccessStatus>(onOp);
                    onTask.Wait();

                    LogReconnect("Windows Master Bluetooth Radio toggled ON successfully.");
                    Thread.Sleep(2000);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogError("WinRT Radio exception: " + ex.Message);
            }
            return false;
        }

        private static void ExecutePowerShell(string script)
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

        private static void RunSetupMode(string configFile)
        {
            Console.WriteLine("=============================================");
            Console.WriteLine("     EasyHFP - Bluetooth Headset Setup       ");
            Console.WriteLine("=============================================");
            LogInfo("Scanning for paired Bluetooth devices...\n");

            List<BtDeviceItem> devices = EnumerateBluetoothDevices();

            if (devices.Count == 0)
            {
                LogError("No paired Bluetooth devices found!");
                Console.WriteLine("Please pair your Bluetooth headset in Windows settings and try again.\nPress Enter to exit...");
                Console.ReadLine();
                return;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                BtDeviceItem dev = devices[i];
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(string.Format("[{0}] {1} (MAC: {2}) - Status: {3}", i + 1, dev.Name, dev.Mac, dev.Connected ? "Connected" : "Disconnected"));
                Console.ResetColor();
            }

            int choice = 0;
            while (choice < 1 || choice > devices.Count)
            {
                Console.Write(string.Format("\nPlease select your Bluetooth headset number (1-{0}): ", devices.Count));
                string input = Console.ReadLine();
                int.TryParse(input, out choice);
            }

            BtDeviceItem selected = devices[choice - 1];

            try
            {
                string content = string.Format("MAC={0}\r\nNAME={1}\r\n", selected.Mac, selected.Name);
                File.WriteAllText(configFile, content);

                LogSuccess(string.Format("Selected Device: {0} (MAC: {1})", selected.Name, selected.Mac));
                LogInfo(string.Format("Configuration file saved: {0}", configFile));
                LogInfo("EasyHFP will now automatically target this device on future runs.\n");

                ShowNotification("EasyHFP Setup Complete", string.Format("Default headset saved: {0}", selected.Name));
                Thread.Sleep(2000);
            }
            catch (Exception ex)
            {
                LogError("Error writing config file: " + ex.Message);
                Thread.Sleep(2000);
            }
        }

        private static List<BtDeviceItem> EnumerateBluetoothDevices()
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
                    list.Add(new BtDeviceItem
                    {
                        Name = deviceInfo.szName,
                        Mac = deviceInfo.Address.ullLong.ToString("X12"),
                        MacUlong = deviceInfo.Address.ullLong,
                        Connected = deviceInfo.fConnected,
                        Info = deviceInfo
                    });
                } while (BluetoothFindNextDevice(hFind, ref deviceInfo));

                BluetoothFindDeviceClose(hFind);
            }

            return list;
        }

        private static void ShowNotification(string title, string message)
        {
            try
            {
                using (NotifyIcon notify = new NotifyIcon())
                {
                    notify.Icon = SystemIcons.Information;
                    notify.Visible = true;
                    notify.ShowBalloonTip(4000, title, message, ToolTipIcon.Info);
                    Thread.Sleep(1000);
                }
            }
            catch { }
        }
    }
}
