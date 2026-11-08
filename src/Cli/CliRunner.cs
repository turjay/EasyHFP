using System;
using System.Collections.Generic;
using System.Threading;
using EasyHFP.Interop;
using EasyHFP.Models;
using EasyHFP.Services;

namespace EasyHFP.Cli
{
    public class CliRunner
    {
        private readonly IBluetoothService _bluetoothService;
        private readonly IConfigurationService _configService;

        public CliRunner(IBluetoothService bluetoothService, IConfigurationService configService)
        {
            _bluetoothService = bluetoothService;
            _configService = configService;
        }

        public int Run(ParsedCliOptions options)
        {
            if (!options.IsValid)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] {options.ErrorMessage}");
                Console.ResetColor();
                return 1;
            }

            switch (options.Action)
            {
                case CliAction.Help:
                    ShowHelp();
                    return 0;

                case CliAction.List:
                    return RunList();

                case CliAction.Select:
                    return RunSelect();

                case CliAction.RadioToggleOnly:
                    return RunRadioToggle();

                case CliAction.Status:
                case CliAction.Toggle:
                case CliAction.Enable:
                case CliAction.Disable:
                    return RunDeviceAction(options);

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] Unhandled CLI action.");
                    Console.ResetColor();
                    return 1;
            }
        }

        private int RunList()
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=========================================================================");
            Console.WriteLine("               EasyHFP - Paired Bluetooth Audio Devices                  ");
            Console.WriteLine("=========================================================================");
            Console.ResetColor();

            var devices = _bluetoothService.EnumerateDevices();
            if (devices.Count == 0)
            {
                Console.WriteLine("No paired Bluetooth audio devices found.");
                return 0;
            }

            var cfg = _configService.LoadConfig();
            for (int i = 0; i < devices.Count; i++)
            {
                var dev = devices[i];
                bool isDef = dev.Mac.Equals(cfg.DefaultMac, StringComparison.OrdinalIgnoreCase);

                Console.ForegroundColor = isDef ? ConsoleColor.Yellow : ConsoleColor.White;
                Console.Write($"[{i + 1}] {dev.Name} ");
                if (isDef) Console.Write("(Default) ");

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write($"[MAC: {dev.Mac}] ");

                Console.ForegroundColor = dev.Connected ? ConsoleColor.Green : ConsoleColor.DarkGray;
                Console.Write($"[{(dev.Connected ? "Connected" : "Disconnected")}] ");

                switch (dev.HfpState)
                {
                    case HfpServiceState.Enabled:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[HFP Service: ENABLED]");
                        break;
                    case HfpServiceState.Disabled:
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine("[HFP Service: DISABLED]");
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("[HFP Service: UNKNOWN]");
                        break;
                }
                Console.ResetColor();
            }

            Console.WriteLine("=========================================================================\n");
            return 0;
        }

        private int RunSelect()
        {
            var devices = _bluetoothService.EnumerateDevices();
            if (devices.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] No paired Bluetooth devices found to select from.");
                Console.ResetColor();
                return 2;
            }

            Console.WriteLine("\nSelect Default Headset:");
            for (int i = 0; i < devices.Count; i++)
            {
                Console.WriteLine($" [{i + 1}] {devices[i].Name} (MAC: {devices[i].Mac})");
            }

            int choice = 0;
            while (choice < 1 || choice > devices.Count)
            {
                Console.Write($"Enter choice (1-{devices.Count}): ");
                string? input = Console.ReadLine();

                // EOF handling (e.g. piped input or closed console)
                if (input == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n[ERROR] Input stream closed (EOF). Setup aborted.");
                    Console.ResetColor();
                    return 1;
                }

                int.TryParse(input.Trim(), out choice);
            }

            var sel = devices[choice - 1];
            var config = _configService.LoadConfig();
            config.DefaultMac = sel.Mac;
            config.DefaultName = sel.Name;
            config.KnownDevices[sel.Mac] = sel.Name;

            try
            {
                _configService.SaveConfig(config);
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] Default headset set to: {sel.Name} [{sel.Mac}]\n");
                Console.ResetColor();
                return 0;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Failed to save configuration: {ex.Message}");
                Console.ResetColor();
                return 1;
            }
        }

        private int RunRadioToggle()
        {
            Console.WriteLine("[INFO] Cycling Bluetooth radio...");
            bool ok = _bluetoothService.RestartBluetoothRadio(out string? err);
            if (ok)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[SUCCESS] Bluetooth radio reset completed.");
                Console.ResetColor();
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Bluetooth radio reset failed: {err}");
                Console.ResetColor();
                return 3;
            }
        }

        private int RunDeviceAction(ParsedCliOptions options)
        {
            var config = _configService.LoadConfig();
            var devices = _bluetoothService.EnumerateDevices();

            BtDeviceItem? targetDev = null;

            if (!string.IsNullOrEmpty(options.TargetDevice))
            {
                targetDev = _bluetoothService.FindDevice(options.TargetDevice, devices, out string? ambiguityError);
                if (ambiguityError != null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] {ambiguityError}");
                    Console.ResetColor();
                    return 2;
                }

                if (targetDev == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Specified device '{options.TargetDevice}' was not found among paired Bluetooth devices!");
                    Console.ResetColor();
                    return 2;
                }
            }
            else
            {
                // In automation mode, never trigger an interactive wizard automatically
                if (string.IsNullOrEmpty(config.DefaultMac))
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[ERROR] No default Bluetooth headset is configured. Specify a device with --device <MAC|NAME> or run 'EasyHFP --select' to configure a default headset.");
                    Console.ResetColor();
                    return 2;
                }

                targetDev = _bluetoothService.FindDevice(config.DefaultMac, devices, out _);
                if (targetDev == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Saved default headset '{config.DefaultName}' [{config.DefaultMac}] was not found among paired Bluetooth devices. Run 'EasyHFP --select' or pass --device <MAC|NAME>.");
                    Console.ResetColor();
                    return 2;
                }
            }

            // Fresh query of current HFP state
            var devInfo = targetDev.Info;
            var currentHfpState = _bluetoothService.QueryHfpState(ref devInfo);
            targetDev.HfpState = currentHfpState;

            if (options.Action == CliAction.Status)
            {
                return PrintStatus(targetDev);
            }

            TransitionIntent intent = options.Action switch
            {
                CliAction.Enable => TransitionIntent.Enable,
                CliAction.Disable => TransitionIntent.Disable,
                _ => TransitionIntent.Toggle
            };

            // Determine reconnect mode
            ReconnectMode reconnectMode = options.ReconnectModeOverride ??
                (ReconnectModeExtensions.TryParseReconnectMode(config.ReconnectMode, out var m) ? m : ReconnectMode.Auto);

            // Take immutable snapshot with user intent
            var snapshot = new BluetoothDeviceSnapshot(
                Mac: targetDev.Mac,
                Name: targetDev.Name,
                MacUlong: targetDev.MacUlong,
                Connected: targetDev.Connected,
                Intent: intent,
                ReconnectMode: reconnectMode,
                DeviceInfo: devInfo,
                ObservedHfpState: currentHfpState
            );

            string intentDesc = intent switch
            {
                TransitionIntent.Enable => "ENABLED",
                TransitionIntent.Disable => "DISABLED",
                _ => "TOGGLE"
            };
            Console.WriteLine($"[INFO] Requesting HFP service {intentDesc} for {targetDev.Name} [Strategy: {reconnectMode.ToConfigString()}]");

            var result = _bluetoothService.ExecuteTransition(snapshot);

            // Output detailed execution breakdown
            if (result.Success)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[SUCCESS] Switched {targetDev.Name} successfully!");
                Console.ResetColor();

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine($"[DIAGNOSTICS] Total: {result.TotalDuration.TotalMilliseconds:F0}ms (ServiceCall: {result.ServiceCallDuration.TotalMilliseconds:F0}ms, Verification: {result.VerificationDuration.TotalMilliseconds:F0}ms, Recovery: {result.ReconnectStrategyUsed})");
                Console.ResetColor();

                if (!options.Silent)
                {
                    Thread.Sleep(500);
                }
                return 0;
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[ERROR] Failed to switch profile for {targetDev.Name}: {result.ErrorMessage}");
                if (!string.IsNullOrEmpty(result.DiagnosticDetails))
                {
                    Console.WriteLine($"[DETAILS] {result.DiagnosticDetails}");
                }
                Console.ResetColor();
                return 3;
            }
        }

        private int PrintStatus(BtDeviceItem dev)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("          EasyHFP Device Status Report           ");
            Console.WriteLine("=================================================");
            Console.ResetColor();
            Console.WriteLine($"Device Name : {dev.Name}");
            Console.WriteLine($"MAC Address : {dev.Mac}");
            Console.WriteLine($"Link Status : {(dev.Connected ? "Connected" : "Disconnected")}");
            Console.Write("HFP Service : ");

            switch (dev.HfpState)
            {
                case HfpServiceState.Enabled:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("ENABLED (Hands-Free Profile active in Windows)");
                    break;
                case HfpServiceState.Disabled:
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine("DISABLED (A2DP high-quality stereo enabled)");
                    break;
                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("UNKNOWN (Unable to query installed services)");
                    break;
            }

            Console.ResetColor();
            Console.WriteLine("=================================================\n");
            return 0;
        }

        private void ShowHelp()
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
            Console.WriteLine("  --on, --enable          Enable Hands-Free Telephony (Microphone)");
            Console.WriteLine("  --off, --disable        Disable Hands-Free Telephony (A2DP Stereo Mode)");
            Console.WriteLine("  --status                Query & display current HFP profile state");
            Console.WriteLine("  --list, -l              List all paired Bluetooth audio devices");
            Console.WriteLine("  --select, -s            Launch interactive setup wizard");
            Console.WriteLine("  --help, -h              Display this documentation\n");
            Console.WriteLine("OPTIONS:");
            Console.WriteLine("  --device, -d <MAC|NAME> Target a specific headset instead of default config");
            Console.WriteLine("  --reconnect-mode, -m    Strategy: 'auto' (default, zero reset), 'pnp', 'none', 'radio'");
            Console.WriteLine("  --silent                Suppress exit pause\n");
            Console.WriteLine("=========================================================================\n");
        }
    }
}
