using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;
using NativeProfileSwitch.Models;

namespace NativeProfileSwitch.Diagnostics
{
    public static class StructuredDiagnosticLogger
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public static string GetDefaultLogDirectory()
        {
            // Place logs inside experiments/NativeProfileSwitch/logs relative to build output
            string baseDir = AppContext.BaseDirectory;
            string candidate = Path.GetFullPath(Path.Combine(baseDir, "..", "..", "..", "..", "..", "logs"));
            if (Directory.Exists(Path.GetDirectoryName(candidate)))
            {
                return candidate;
            }
            return Path.Combine(baseDir, "logs");
        }

        public static ExperimentRunReport CreateInitializedReport(string experimentName, HeadsetEndpointGroup? targetGroup = null)
        {
            var (productName, displayVersion, buildNumber, isWin11) = QueryWindowsOsDetails();

            return new ExperimentRunReport
            {
                ExperimentName = experimentName,
                StartTimeUtc = DateTime.UtcNow,
                WindowsProductName = productName,
                WindowsDisplayVersion = displayVersion,
                WindowsBuildNumber = buildNumber,
                IsWindows11 = isWin11,
                DeviceName = targetGroup?.DisplayName,
                ContainerId = targetGroup?.ContainerId,
                BluetoothMac = targetGroup?.BluetoothMac,
                Outcome = ExperimentOutcome.NOT_RUN
            };
        }

        public static (string productName, string displayVersion, int buildNumber, bool isWin11) QueryWindowsOsDetails()
        {
            int build = Environment.OSVersion.Version.Build;
            string displayVer = string.Empty;
            string regProduct = "Windows";

            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion", false);
                if (key != null)
                {
                    regProduct = key.GetValue("ProductName") as string ?? regProduct;
                    displayVer = key.GetValue("DisplayVersion") as string ?? string.Empty;
                    if (int.TryParse(key.GetValue("CurrentBuildNumber")?.ToString(), out int parsedBuild))
                    {
                        build = parsedBuild;
                    }
                }
            }
            catch { }

            bool isWin11 = build >= 22000;
            string effectiveName = isWin11 && regProduct.Contains("Windows 10", StringComparison.OrdinalIgnoreCase)
                ? regProduct.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase)
                : regProduct;

            return (effectiveName, displayVer, build, isWin11);
        }

        public static (string jsonPath, string textPath) SaveReport(ExperimentRunReport report, string? customOutputDirectory = null)
        {
            string logDir = string.IsNullOrWhiteSpace(customOutputDirectory)
                ? GetDefaultLogDirectory()
                : customOutputDirectory;

            if (!Directory.Exists(logDir))
            {
                Directory.CreateDirectory(logDir);
            }

            if (report.EndTimeUtc == default)
            {
                report.EndTimeUtc = DateTime.UtcNow;
            }

            string safeExpName = string.Join("_", report.ExperimentName.Split(Path.GetInvalidFileNameChars()));
            string prefix = $"{report.StartTimeUtc:yyyyMMdd_HHmmss}_{safeExpName}_{report.RunId.Substring(0, 8)}";

            string jsonPath = Path.Combine(logDir, $"{prefix}.json");
            string textPath = Path.Combine(logDir, $"{prefix}.log");

            string json = JsonSerializer.Serialize(report, JsonOptions);
            File.WriteAllText(jsonPath, json, Encoding.UTF8);

            string textSummary = FormatTextReport(report);
            File.WriteAllText(textPath, textSummary, Encoding.UTF8);

            return (jsonPath, textPath);
        }

        public static string FormatTextReport(ExperimentRunReport report)
        {
            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"EasyHFP Phase 2 Experiment Report: {report.ExperimentName}");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Run ID                  : {report.RunId}");
            sb.AppendLine($"Start Time (UTC)        : {report.StartTimeUtc:O}");
            sb.AppendLine($"End Time (UTC)          : {report.EndTimeUtc:O}");
            sb.AppendLine($"OS Version              : {report.WindowsProductName} {report.WindowsDisplayVersion} (Build {report.WindowsBuildNumber}, IsWindows11={report.IsWindows11})");
            sb.AppendLine($"Target Device           : {report.DeviceName ?? "N/A"}");
            sb.AppendLine($"Container ID            : {report.ContainerId?.ToString("B") ?? "N/A"}");
            sb.AppendLine($"Bluetooth MAC           : {report.BluetoothMac ?? "N/A"}");
            sb.AppendLine($"Render Endpoint ID      : {report.SelectedRenderEndpointId ?? "N/A"}");
            sb.AppendLine($"Capture Endpoint ID     : {report.SelectedCaptureEndpointId ?? "N/A"}");
            sb.AppendLine($"Stream Direction        : {report.StreamDirection}");
            sb.AppendLine($"Stream Category         : {report.StreamCategory}");
            sb.AppendLine($"OUTCOME                 : {report.Outcome}");
            sb.AppendLine($"Transition to HFP (ms)  : {(report.TransitionToHfpDurationMs.HasValue ? $"{report.TransitionToHfpDurationMs.Value:F1} ms" : "N/A")}");
            sb.AppendLine($"Return to A2DP (ms)     : {(report.ReturnToA2dpDurationMs.HasValue ? $"{report.ReturnToA2dpDurationMs.Value:F1} ms" : "N/A")}");
            sb.AppendLine();

            AppendSnapshot(sb, "Baseline Verification", report.BaselineVerification);
            AppendSnapshot(sb, "Active Stream Verification", report.ActiveStreamVerification);
            AppendSnapshot(sb, "Post-Release Verification", report.PostReleaseVerification);

            sb.AppendLine("--- API Calls ---");
            foreach (var call in report.ApiCalls)
            {
                sb.AppendLine($"  [{call.TimestampUtc:HH:mm:ss.fff}] {call.ApiName} -> {call.HResultFormatted} | {call.Notes}");
            }
            sb.AppendLine();

            sb.AppendLine($"--- Core Audio Notification Events ({report.NotificationEvents.Count}) ---");
            foreach (var ev in report.NotificationEvents)
            {
                sb.AppendLine($"  [{ev.TimestampUtc:HH:mm:ss.fff}] {ev.EventType} on {ev.EndpointId}: {ev.Details}");
            }
            sb.AppendLine();

            if (report.Notes.Count > 0)
            {
                sb.AppendLine("--- Notes ---");
                foreach (var n in report.Notes)
                {
                    sb.AppendLine($"  * {n}");
                }
                sb.AppendLine();
            }

            if (report.Errors.Count > 0)
            {
                sb.AppendLine("--- Errors ---");
                foreach (var err in report.Errors)
                {
                    sb.AppendLine($"  [ERROR] {err}");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }

        private static void AppendSnapshot(StringBuilder sb, string title, ProfileVerificationSnapshot? snap)
        {
            sb.AppendLine($"--- {title} ---");
            if (snap == null)
            {
                sb.AppendLine("  (Not captured)");
                sb.AppendLine();
                return;
            }

            sb.AppendLine($"  Timestamp (UTC)           : {snap.TimestampUtc:HH:mm:ss.fff}");
            sb.AppendLine($"  Render State              : {snap.RenderStateText}");
            sb.AppendLine($"  Capture State             : {snap.CaptureStateText}");
            sb.AppendLine($"  Render DefaultMixFormat   : {snap.RenderDefaultMixFormat?.Summary ?? "N/A"}");
            sb.AppendLine($"  Render CommMixFormat      : {snap.RenderCommunicationsMixFormat?.Summary ?? "N/A"}");
            sb.AppendLine($"  Capture DeviceFormat      : {snap.CaptureDeviceFormat?.Summary ?? "N/A"}");
            sb.AppendLine($"  Active Capture Sessions   : Self={snap.SelfActiveCaptureSessions}, External={snap.ExternalActiveCaptureSessions} (PIDs: [{string.Join(", ", snap.ExternalCaptureProcessIds)}])");
            sb.AppendLine($"  Active Render Sessions    : Self={snap.SelfActiveRenderSessions}, External={snap.ExternalActiveRenderSessions}");
            sb.AppendLine($"  Inferred Transport Profile: {snap.InferredProfile}");
            sb.AppendLine($"  Verification Confidence   : {snap.Confidence}");
            foreach (var ev in snap.EvidenceSummary)
            {
                sb.AppendLine($"    - {ev}");
            }
            sb.AppendLine();
        }
    }
}
