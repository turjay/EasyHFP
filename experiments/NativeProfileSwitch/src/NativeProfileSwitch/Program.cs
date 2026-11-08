using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NativeProfileSwitch.Diagnostics;
using NativeProfileSwitch.Discovery;
using NativeProfileSwitch.Experiments;
using NativeProfileSwitch.Interop;
using NativeProfileSwitch.Models;

namespace NativeProfileSwitch
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Console.WriteLine("\n[INFO] Cancellation requested (Ctrl+C). Stopping active stream and releasing COM resources...");
                cts.Cancel();
            };

            if (args.Length == 0 || args.Any(a => a.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                                                  a.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                                                  a.Equals("/?", StringComparison.OrdinalIgnoreCase)))
            {
                PrintHelp();
                return 0;
            }

            string action = args[0].ToLowerInvariant();
            string? targetFilter = GetOptionValue(args, "--device", "-d");
            int holdMs = GetIntOptionValue(args, "--hold-ms", 3000);
            int postMs = GetIntOptionValue(args, "--post-ms", 1000);
            int iterations = GetIntOptionValue(args, "--iterations", 3);
            bool activeOnly = args.Any(a => a.Equals("--active-only", StringComparison.OrdinalIgnoreCase));
            bool explicitConfirm = args.Any(a => a.Equals("--confirm", StringComparison.OrdinalIgnoreCase));
            bool initOnly = args.Any(a => a.Equals("--init-only", StringComparison.OrdinalIgnoreCase));
            string? customLogDir = GetOptionValue(args, "--log-dir", "-o");

            var discovery = new EndpointDiscoveryService();

            if (action == "--enumerate" || action == "--discover" || action == "-l")
            {
                return RunEnumeration(discovery, activeOnly, customLogDir);
            }

            // All stream/switching experiments require explicit confirmation (--confirm or interactive 'y')
            if (!EnsureExplicitUserConfirmation(action, explicitConfirm))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[NOT_RUN] Experiment cancelled: hardware stream tests require explicit user initiation (pass --confirm or type 'y').");
                Console.ResetColor();
                return 1;
            }

            uint stateMask = (action == "--test-e")
                ? CoreAudioInterop.DEVICE_STATEMASK_ALL
                : CoreAudioInterop.DEVICE_STATE_ACTIVE;

            var endpoints = discovery.EnumerateAllEndpoints(stateMask, queryMixFormatCapabilities: true);
            var groups = discovery.GroupEndpointsByPhysicalDevice(endpoints);

            var targetGroup = SelectTargetHeadsetGroup(groups, targetFilter);
            if (targetGroup == null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ERROR] No matching Bluetooth audio endpoint group found. Run '--enumerate' to inspect available endpoints.");
                Console.ResetColor();
                return 2;
            }

            PrintSelectedGroupHeader(targetGroup);

            var harness = new ExperimentHarness();

            switch (action)
            {
                case "--exp-a":
                case "--test-a":
                {
                    var report = harness.RunTestA_CaptureSwitch(targetGroup, holdMs, postMs, cts.Token);
                    OutputAndSaveReport(report, customLogDir);
                    return report.Outcome == ExperimentOutcome.TRANSITION_FAILED ? 3 : 0;
                }

                case "--exp-b":
                case "--test-b":
                {
                    var report = harness.RunTestB_CommunicationsRenderSwitch(targetGroup, holdMs, postMs, !initOnly, cts.Token);
                    OutputAndSaveReport(report, customLogDir);
                    return report.Outcome == ExperimentOutcome.TRANSITION_FAILED ? 3 : 0;
                }

                case "--test-c":
                {
                    string mode = GetOptionValue(args, "--mode", "-m") ?? "capture";
                    bool useCapture = !mode.Equals("render", StringComparison.OrdinalIgnoreCase);
                    var reports = harness.RunTestC_RepeatedSwitching(targetGroup, iterations, holdMs, postMs, useCapture, cts.Token);
                    foreach (var rep in reports)
                    {
                        OutputAndSaveReport(rep, customLogDir);
                    }
                    return reports.Any(r => r.Outcome == ExperimentOutcome.TRANSITION_FAILED) ? 3 : 0;
                }

                case "--test-d":
                {
                    var report = harness.RunTestD_ExternalMicrophoneOwnerCheck(targetGroup, holdMs, postMs, cts.Token);
                    OutputAndSaveReport(report, customLogDir);
                    return report.Outcome == ExperimentOutcome.TRANSITION_FAILED ? 3 : 0;
                }

                case "--test-e":
                {
                    var report = harness.RunTestE_RecoveryAndInvalidEndpointHandling(targetGroup, holdMs, cts.Token);
                    OutputAndSaveReport(report, customLogDir);
                    return 0;
                }

                default:
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[ERROR] Unknown command '{action}'. Run with --help for usage.");
                    Console.ResetColor();
                    return 1;
            }
        }

        private static int RunEnumeration(EndpointDiscoveryService discovery, bool activeOnly, string? customLogDir)
        {
            var (productName, displayVersion, buildNumber, isWin11) = StructuredDiagnosticLogger.QueryWindowsOsDetails();
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("================================================================================");
            Console.WriteLine("       EasyHFP Phase 2 — Core Audio Endpoint Discovery & Mapping (Read-Only)    ");
            Console.WriteLine("================================================================================");
            Console.ResetColor();
            Console.WriteLine($"OS Environment : {productName} {displayVersion} (Build {buildNumber}, IsWindows11={isWin11})");

            uint mask = activeOnly ? CoreAudioInterop.DEVICE_STATE_ACTIVE : CoreAudioInterop.DEVICE_STATEMASK_ALL;
            var endpoints = discovery.EnumerateAllEndpoints(mask, queryMixFormatCapabilities: true);
            var groups = discovery.GroupEndpointsByPhysicalDevice(endpoints);

            Console.WriteLine($"Total Endpoints: {endpoints.Count} across {groups.Count} physical device container group(s)\n");

            foreach (var g in groups.OrderByDescending(x => x.IsBluetoothClassic || x.IsBluetoothLeAudio))
            {
                Console.ForegroundColor = (g.IsBluetoothClassic || g.IsBluetoothLeAudio) ? ConsoleColor.Yellow : ConsoleColor.White;
                Console.WriteLine($"[Group] {g.DisplayName}");
                Console.ResetColor();
                Console.WriteLine($"  Container ID       : {g.ContainerId?.ToString("B") ?? "N/A"}");
                Console.WriteLine($"  Association Method : {g.AssociationMethod}");
                Console.WriteLine($"  Bluetooth MAC      : {g.BluetoothMac ?? "N/A"}");
                Console.WriteLine($"  Bluetooth Type     : {(g.IsBluetoothLeAudio ? "LE Audio" : (g.IsBluetoothClassic ? "Bluetooth Classic" : "Non-Bluetooth"))}");
                if (g.IsBluetoothClassic)
                {
                    Console.WriteLine($"  Win11 Unified Topo : {g.IsWindows11UnifiedEndpointTopology}");
                }
                if (g.HasAmbiguity || !string.IsNullOrEmpty(g.AmbiguityNotes))
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"  Topology/Ambiguity : {g.AmbiguityNotes}");
                    Console.ResetColor();
                }

                foreach (var ep in g.RenderEndpoints.Concat(g.CaptureEndpoints))
                {
                    Console.WriteLine($"    [{ep.DataFlow.ToUpperInvariant()}] {ep.FriendlyName} ({ep.StateText})");
                    Console.WriteLine($"      Endpoint ID        : {ep.EndpointId}");
                    Console.WriteLine($"      Instance ID        : {ep.DeviceInstanceId ?? "N/A"}");
                    Console.WriteLine($"      Transport Kind     : {ep.BluetoothTransportKind}");
                    Console.WriteLine($"      Default Roles      : {(ep.DefaultRoles.Count > 0 ? string.Join(", ", ep.DefaultRoles) : "None")}");
                    Console.WriteLine($"      Speaker Mask       : {(ep.PhysicalSpeakersMask.HasValue ? $"0x{ep.PhysicalSpeakersMask.Value:X}" : "N/A")}");
                    Console.WriteLine($"      PropStore Format   : {ep.PropertyStoreDeviceFormat?.Summary ?? "N/A"}");
                    Console.WriteLine($"      Default MixFormat  : {ep.DefaultMixFormat?.Summary ?? "N/A"}");
                    Console.WriteLine($"      Comm MixFormat     : {ep.CommunicationsMixFormat?.Summary ?? "N/A"}");
                    Console.WriteLine($"      Active Sessions    : Total={ep.TotalSessionCount}, Active={ep.ActiveSessionCount} (Self={ep.SelfActiveSessionCount}, External={ep.ExternalActiveSessionCount})");
                }
                Console.WriteLine();
            }

            string logDir = string.IsNullOrWhiteSpace(customLogDir)
                ? StructuredDiagnosticLogger.GetDefaultLogDirectory()
                : customLogDir;
            Directory.CreateDirectory(logDir);
            string snapshotPath = Path.Combine(logDir, $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_EndpointDiscovery.json");
            File.WriteAllText(snapshotPath, JsonSerializer.Serialize(groups, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine($"[INFO] Saved read-only discovery report to: {snapshotPath}");
            return 0;
        }

        private static HeadsetEndpointGroup? SelectTargetHeadsetGroup(List<HeadsetEndpointGroup> groups, string? filter)
        {
            if (!string.IsNullOrWhiteSpace(filter))
            {
                string cleanFilter = filter.Replace(":", "").Replace("-", "").Trim();
                return groups.FirstOrDefault(g =>
                    (g.BluetoothMac != null && g.BluetoothMac.Equals(cleanFilter, StringComparison.OrdinalIgnoreCase)) ||
                    (g.ContainerId.HasValue && g.ContainerId.Value.ToString("D").Equals(filter.Trim('{', '}'), StringComparison.OrdinalIgnoreCase)) ||
                    g.DisplayName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    g.RenderEndpoints.Any(e => e.EndpointId.Equals(filter, StringComparison.OrdinalIgnoreCase) || e.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase)) ||
                    g.CaptureEndpoints.Any(e => e.EndpointId.Equals(filter, StringComparison.OrdinalIgnoreCase) || e.FriendlyName.Contains(filter, StringComparison.OrdinalIgnoreCase)));
            }

            // Default: pick the first Bluetooth Classic group that has both active Render and Capture endpoints,
            // or any Bluetooth Classic group.
            return groups.FirstOrDefault(g => g.IsBluetoothClassic &&
                                              g.RenderEndpoints.Any(r => r.RawState == CoreAudioInterop.DEVICE_STATE_ACTIVE) &&
                                              g.CaptureEndpoints.Any(c => c.RawState == CoreAudioInterop.DEVICE_STATE_ACTIVE))
                   ?? groups.FirstOrDefault(g => g.IsBluetoothClassic);
        }

        private static bool EnsureExplicitUserConfirmation(string action, bool explicitConfirmFlag)
        {
            if (explicitConfirmFlag) return true;

            if (Console.IsInputRedirected)
            {
                return false;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write($"[CONFIRM] Execute hardware audio stream experiment '{action}'? (y/N): ");
            Console.ResetColor();
            string? resp = Console.ReadLine();
            return resp != null && (resp.Trim().Equals("y", StringComparison.OrdinalIgnoreCase) ||
                                    resp.Trim().Equals("yes", StringComparison.OrdinalIgnoreCase));
        }

        private static void PrintSelectedGroupHeader(HeadsetEndpointGroup group)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.WriteLine($"Selected Headset Group : {group.DisplayName}");
            Console.WriteLine($"Container ID           : {group.ContainerId?.ToString("B") ?? "N/A"} ({group.AssociationMethod})");
            Console.WriteLine($"Bluetooth MAC          : {group.BluetoothMac ?? "N/A"}");
            Console.WriteLine("--------------------------------------------------------------------------------");
            Console.ResetColor();
        }

        private static void OutputAndSaveReport(ExperimentRunReport report, string? customLogDir)
        {
            string summary = StructuredDiagnosticLogger.FormatTextReport(report);
            Console.WriteLine(summary);

            var (jsonPath, textPath) = StructuredDiagnosticLogger.SaveReport(report, customLogDir);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[SAVED] JSON Report : {jsonPath}");
            Console.WriteLine($"[SAVED] Text Log    : {textPath}");
            Console.ResetColor();
        }

        private static string? GetOptionValue(string[] args, string primaryFlag, string shortFlag)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(primaryFlag, StringComparison.OrdinalIgnoreCase) ||
                    args[i].Equals(shortFlag, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }

        private static int GetIntOptionValue(string[] args, string flag, int defaultValue)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i].Equals(flag, StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(args[i + 1], out int parsed))
                {
                    return parsed;
                }
            }
            return defaultValue;
        }

        private static void PrintHelp()
        {
            Console.WriteLine(@"
================================================================================
  EasyHFP Phase 2 — Native Windows A2DP/HFP Profile Switch Experiment Harness
================================================================================
Usage:
  NativeProfileSwitch.exe <COMMAND> [OPTIONS]

COMMANDS (Read-Only):
  --enumerate, -l         Enumerate all Core Audio endpoints, ContainerId groups,
                          Bluetooth transport types, and mix format capabilities.
                          (Read-only; never opens streams or modifies routing.)

COMMANDS (Hardware Stream Experiments — require --confirm or interactive 'y'):
  --test-a, --exp-a       Experiment A: Shared-mode WASAPI microphone capture
                          (drains & discards packets in-place; zero recording).
  --test-b, --exp-b       Experiment B: Shared-mode WASAPI render stream with
                          IAudioClient2::SetClientProperties(AudioCategory_Communications).
  --test-c                Repeatable sequential switching test (--iterations N).
  --test-d                External microphone owner contention & verification test.
  --test-e                Recovery & invalidated/unplugged endpoint handling test.

OPTIONS:
  --device, -d <filter>   Select target headset by Name, MAC, ContainerId, or EndpointId.
  --active-only           Only list DEVICE_STATE_ACTIVE endpoints during --enumerate.
  --hold-ms <ms>          Duration to hold active stream in ms (default: 3000).
  --post-ms <ms>          Post-release observation window in ms (default: 1000).
  --iterations <count>    Number of cycles for --test-c (default: 3).
  --mode, -m <mode>       Approach for --test-c: 'capture' (default) or 'render'.
  --init-only             For --test-b: test SetClientProperties + Initialize without Start().
  --confirm               Confirm explicit user intent for hardware stream execution.
  --log-dir, -o <path>    Directory for structured JSON and text logs.
================================================================================
");
        }
    }
}
