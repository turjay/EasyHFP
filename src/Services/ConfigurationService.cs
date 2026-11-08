using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using EasyHFP.Models;
using Microsoft.Win32;

namespace EasyHFP.Services
{
    public interface IConfigurationService
    {
        string ConfigFilePath { get; }
        ConfigData LoadConfig();
        void SaveConfig(ConfigData config);
        bool IsAutoStartEnabled();
        void SetAutoStart(bool enable);
        bool RepairAutoStartIfStale(bool startWithWindowsPreference);
        string NormalizeMac(string mac);
    }

    public class ConfigurationService : IConfigurationService
    {
        public const string DefaultRunRegistrySubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string AutoStartValueName = "EasyHFP";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        private readonly string _configFilePath;
        private readonly string _appDataLegacyConfigFilePath;
        private readonly string _legacyConfigFilePath;
        private readonly string _runRegistryKeyPath;
        private readonly string? _customExecutablePath;

        public string ConfigFilePath => _configFilePath;

        public ConfigurationService(
            string? customPath = null,
            string? legacyPath = null,
            string? runRegistryKeyPath = null,
            string? customExecutablePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                _configFilePath = customPath;
                _appDataLegacyConfigFilePath = customPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                    ? Path.Combine(Path.GetDirectoryName(customPath) ?? string.Empty, "config.txt")
                    : string.Empty;
                _legacyConfigFilePath = legacyPath ?? string.Empty;
            }
            else
            {
                string appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EasyHFP");
                _configFilePath = Path.Combine(appDataDir, "config.json");
                _appDataLegacyConfigFilePath = Path.Combine(appDataDir, "config.txt");
                _legacyConfigFilePath = legacyPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
            }

            _runRegistryKeyPath = string.IsNullOrWhiteSpace(runRegistryKeyPath)
                ? DefaultRunRegistrySubKey
                : runRegistryKeyPath;
            _customExecutablePath = customExecutablePath;
        }

        public string NormalizeMac(string mac)
        {
            if (string.IsNullOrWhiteSpace(mac)) return string.Empty;
            return mac.Replace(":", "").Replace("-", "").Trim().ToUpperInvariant();
        }

        public static bool IsValidMac(string mac)
        {
            if (string.IsNullOrEmpty(mac) || mac.Length != 12) return false;
            for (int i = 0; i < mac.Length; i++)
            {
                char c = mac[i];
                bool isHex = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'F') || (c >= 'a' && c <= 'f');
                if (!isHex) return false;
            }
            return true;
        }

        public ConfigData LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    return LoadExistingConfigFile(_configFilePath);
                }

                string? legacyCandidate = FindExistingLegacyConfigPath();
                if (!string.IsNullOrEmpty(legacyCandidate))
                {
                    var migrated = ParseLegacyConfigFile(legacyCandidate);
                    migrated.StartWithWindows = IsAutoStartEnabled();
                    migrated.SetupCompleted = IsValidMac(migrated.DefaultMac);

                    try
                    {
                        SaveConfig(migrated);
                        AppLogger.Info(
                            $"Migrated legacy configuration from '{legacyCandidate}' to '{_configFilePath}' " +
                            $"(setupCompleted={migrated.SetupCompleted}). Original legacy file preserved.");
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"Could not persist migrated config to '{_configFilePath}' ({ex.Message}); using migrated settings in memory.");
                    }

                    return migrated;
                }

                return new ConfigData();
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to load configuration from '{_configFilePath}'. Using safe defaults.", ex);
                return new ConfigData();
            }
        }

        private string? FindExistingLegacyConfigPath()
        {
            if (!string.IsNullOrWhiteSpace(_legacyConfigFilePath) && File.Exists(_legacyConfigFilePath))
            {
                return _legacyConfigFilePath;
            }

            if (!string.IsNullOrWhiteSpace(_appDataLegacyConfigFilePath) &&
                !string.Equals(_appDataLegacyConfigFilePath, _configFilePath, StringComparison.OrdinalIgnoreCase) &&
                File.Exists(_appDataLegacyConfigFilePath))
            {
                return _appDataLegacyConfigFilePath;
            }

            return null;
        }

        private ConfigData LoadExistingConfigFile(string filePath)
        {
            string rawContent = File.ReadAllText(filePath, Encoding.UTF8);
            string trimmed = rawContent.TrimStart();

            if (trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<ConfigData>(rawContent, JsonOptions);
                    if (parsed == null)
                    {
                        AppLogger.Warn($"Configuration file '{filePath}' deserialized to null; using safe defaults.");
                        return new ConfigData();
                    }

                    return SanitizeLoadedConfig(parsed);
                }
                catch (JsonException ex)
                {
                    AppLogger.Warn($"Malformed JSON in configuration file '{filePath}': {ex.Message}. Falling back to safe defaults.");
                    return new ConfigData();
                }
            }

            // If a legacy .txt file path was loaded directly, parse KEY=VALUE lines; if a .json file has non-JSON content, treat as malformed JSON.
            if (filePath.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                return ParseLegacyConfigFile(filePath);
            }

            AppLogger.Warn($"Configuration file '{filePath}' does not contain valid JSON. Falling back to safe defaults.");
            return new ConfigData();
        }

        private ConfigData SanitizeLoadedConfig(ConfigData config)
        {
            config.SchemaVersion = config.SchemaVersion <= 0 ? ConfigData.CurrentSchemaVersion : config.SchemaVersion;
            config.Preferences ??= new UserPreferences();

            string candidateMac = NormalizeMac(config.SelectedDeviceId);
            if (!string.IsNullOrEmpty(candidateMac) && !IsValidMac(candidateMac))
            {
                AppLogger.Warn($"Ignored invalid selectedDeviceId in JSON config: '{config.SelectedDeviceId}'.");
                candidateMac = string.Empty;
            }
            config.SelectedDeviceId = candidateMac;

            if (string.IsNullOrWhiteSpace(config.SelectedDeviceName))
            {
                config.SelectedDeviceName = "Bluetooth Headset";
            }
            else
            {
                config.SelectedDeviceName = config.SelectedDeviceName.Trim();
            }

            if (ReconnectModeExtensions.TryParseReconnectMode(config.ReconnectMode ?? "auto", out var parsedMode))
            {
                config.ReconnectMode = parsedMode.ToConfigString();
            }
            else
            {
                config.ReconnectMode = "auto";
            }

            config.Hotkey = string.IsNullOrWhiteSpace(config.Hotkey) ? "Ctrl+Alt+H" : config.Hotkey.Trim();

            var cleanKnownDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (config.KnownDevices != null)
            {
                foreach (var kvp in config.KnownDevices)
                {
                    string normKey = NormalizeMac(kvp.Key);
                    if (IsValidMac(normKey) && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        cleanKnownDevices[normKey] = kvp.Value.Trim();
                    }
                }
            }

            if (!string.IsNullOrEmpty(config.SelectedDeviceId) && !cleanKnownDevices.ContainsKey(config.SelectedDeviceId))
            {
                cleanKnownDevices[config.SelectedDeviceId] = config.SelectedDeviceName;
            }

            config.KnownDevices = cleanKnownDevices;
            return config;
        }

        private ConfigData ParseLegacyConfigFile(string filePath)
        {
            var config = new ConfigData();
            string[] lines = File.ReadAllLines(filePath);
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
                        if (IsValidMac(mac) && !string.IsNullOrWhiteSpace(name))
                        {
                            config.KnownDevices[mac] = name;
                        }
                        else
                        {
                            AppLogger.Warn($"Ignored invalid device entry in config: '{line}'.");
                        }
                    }
                }
                else
                {
                    if (line.StartsWith("MAC=", StringComparison.OrdinalIgnoreCase))
                    {
                        string candidateMac = NormalizeMac(line.Substring(4).Trim());
                        if (string.IsNullOrEmpty(candidateMac) || IsValidMac(candidateMac))
                        {
                            config.DefaultMac = candidateMac;
                        }
                        else
                        {
                            AppLogger.Warn($"Ignored corrupted MAC value in config: '{candidateMac}'.");
                        }
                    }
                    else if (line.StartsWith("NAME=", StringComparison.OrdinalIgnoreCase))
                    {
                        string nameVal = line.Substring(5).Trim();
                        if (!string.IsNullOrEmpty(nameVal))
                        {
                            config.DefaultName = nameVal;
                        }
                    }
                    else if (line.StartsWith("RECONNECT_MODE=", StringComparison.OrdinalIgnoreCase))
                    {
                        string modeStr = line.Substring(15).Trim().ToLowerInvariant();
                        if (ReconnectModeExtensions.TryParseReconnectMode(modeStr, out var parsedMode))
                        {
                            config.ReconnectMode = parsedMode.ToConfigString();
                        }
                        else
                        {
                            AppLogger.Warn($"Invalid RECONNECT_MODE '{modeStr}' in config; defaulting to 'auto'.");
                            config.ReconnectMode = "auto";
                        }
                    }
                    else if (line.StartsWith("SHOW_NOTIFICATIONS=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(line.Substring(19).Trim(), out bool show))
                        {
                            config.ShowNotifications = show;
                        }
                    }
                    else if (line.StartsWith("MINIMIZE_TO_TRAY=", StringComparison.OrdinalIgnoreCase))
                    {
                        if (bool.TryParse(line.Substring(17).Trim(), out bool minTray))
                        {
                            config.MinimizeToTray = minTray;
                        }
                    }
                    else if (line.StartsWith("HOTKEY=", StringComparison.OrdinalIgnoreCase))
                    {
                        string hk = line.Substring(7).Trim();
                        config.Hotkey = string.IsNullOrWhiteSpace(hk) ? "Ctrl+Alt+H" : hk;
                    }
                }
            }

            if (!string.IsNullOrEmpty(config.DefaultMac) && !config.KnownDevices.ContainsKey(config.DefaultMac))
            {
                config.KnownDevices[config.DefaultMac] = config.DefaultName;
            }

            return config;
        }

        private static readonly object _fileLock = new();

        public void SaveConfig(ConfigData config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));

            try
            {
                string dir = Path.GetDirectoryName(_configFilePath)!;
                if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var toSave = config.Clone();
                toSave.SchemaVersion = ConfigData.CurrentSchemaVersion;

                string normalizedDefaultMac = NormalizeMac(toSave.DefaultMac);
                if (!string.IsNullOrEmpty(normalizedDefaultMac) && !IsValidMac(normalizedDefaultMac))
                {
                    normalizedDefaultMac = string.Empty;
                }
                toSave.DefaultMac = normalizedDefaultMac;

                if (string.IsNullOrWhiteSpace(toSave.DefaultName))
                {
                    toSave.DefaultName = "Bluetooth Headset";
                }

                toSave.ReconnectMode = ReconnectModeExtensions.TryParseReconnectMode(toSave.ReconnectMode, out var parsedMode)
                    ? parsedMode.ToConfigString()
                    : "auto";

                toSave.Hotkey = string.IsNullOrWhiteSpace(toSave.Hotkey) ? "Ctrl+Alt+H" : toSave.Hotkey.Trim();

                var cleanDevices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var kvp in toSave.KnownDevices)
                {
                    string normKey = NormalizeMac(kvp.Key);
                    if (IsValidMac(normKey) && !string.IsNullOrWhiteSpace(kvp.Value))
                    {
                        cleanDevices[normKey] = kvp.Value.Trim();
                    }
                }

                if (!string.IsNullOrEmpty(toSave.DefaultMac) && !cleanDevices.ContainsKey(toSave.DefaultMac))
                {
                    cleanDevices[toSave.DefaultMac] = toSave.DefaultName;
                }
                toSave.KnownDevices = cleanDevices;

                string json = JsonSerializer.Serialize(toSave, JsonOptions);

                // Atomic write: write to process-unique temp file, then move/overwrite target
                string tempFile = $"{_configFilePath}.tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";
                lock (_fileLock)
                {
                    try
                    {
                        File.WriteAllText(tempFile, json, Encoding.UTF8);
                        File.Move(tempFile, _configFilePath, overwrite: true);
                    }
                    finally
                    {
                        if (File.Exists(tempFile))
                        {
                            try { File.Delete(tempFile); } catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to save configuration to '{_configFilePath}'.", ex);
                throw new IOException($"Failed to save EasyHFP configuration to '{_configFilePath}': {ex.Message}", ex);
            }
        }

        public static string BuildAutoStartCommand(string executablePath)
        {
            string cleanPath = executablePath.Trim().Trim('"');
            return $"\"{cleanPath}\" --tray";
        }

        public static bool TryExtractExecutablePath(string? rawCommand, out string executablePath)
        {
            executablePath = string.Empty;
            if (string.IsNullOrWhiteSpace(rawCommand))
            {
                return false;
            }

            string trimmed = rawCommand.Trim();
            if (trimmed.StartsWith("\""))
            {
                int closingQuote = trimmed.IndexOf('"', 1);
                if (closingQuote > 1)
                {
                    executablePath = trimmed.Substring(1, closingQuote - 1).Trim();
                    return !string.IsNullOrEmpty(executablePath);
                }
                return false;
            }

            int exeEndIdx = trimmed.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (exeEndIdx > 0)
            {
                executablePath = trimmed.Substring(0, exeEndIdx + 4).Trim();
                return !string.IsNullOrEmpty(executablePath);
            }

            int spaceIdx = trimmed.IndexOf(' ');
            executablePath = (spaceIdx > 0 ? trimmed.Substring(0, spaceIdx) : trimmed).Trim();
            return !string.IsNullOrEmpty(executablePath);
        }

        private string? ResolveExecutablePath()
        {
            if (!string.IsNullOrWhiteSpace(_customExecutablePath))
            {
                return _customExecutablePath.Trim().Trim('"');
            }

            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath))
            {
                using var currentProcess = Process.GetCurrentProcess();
                exePath = currentProcess.MainModule?.FileName;
            }

            return exePath;
        }

        public bool HasAutoStartRegistryValue()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_runRegistryKeyPath, false);
                if (key == null) return false;
                string? rawValue = key.GetValue(AutoStartValueName) as string;
                return !string.IsNullOrWhiteSpace(rawValue);
            }
            catch
            {
                return false;
            }
        }

        public bool IsAutoStartEnabled()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(_runRegistryKeyPath, false);
                if (key == null) return false;

                string? rawValue = key.GetValue(AutoStartValueName) as string;
                if (string.IsNullOrWhiteSpace(rawValue)) return false;

                if (!TryExtractExecutablePath(rawValue, out string registeredExePath))
                {
                    return false;
                }

                if (!File.Exists(registeredExePath))
                {
                    return false;
                }

                string? currentExePath = ResolveExecutablePath();
                if (!string.IsNullOrEmpty(currentExePath))
                {
                    string currentFileName = Path.GetFileName(currentExePath);
                    // When running as EasyHFP.exe (or with a custom test executable path), ensure the registered path matches.
                    if (!string.IsNullOrWhiteSpace(_customExecutablePath) ||
                        currentFileName.Equals("EasyHFP.exe", StringComparison.OrdinalIgnoreCase))
                    {
                        return string.Equals(
                            Path.GetFullPath(registeredExePath),
                            Path.GetFullPath(currentExePath),
                            StringComparison.OrdinalIgnoreCase);
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to check Windows auto-start registry status: {ex.Message}");
                return false;
            }
        }

        public void SetAutoStart(bool enable)
        {
            try
            {
                using var key = Registry.CurrentUser.CreateSubKey(_runRegistryKeyPath, true);
                if (key == null)
                {
                    throw new InvalidOperationException($"Unable to open registry key 'HKCU\\{_runRegistryKeyPath}'.");
                }

                if (enable)
                {
                    string? exePath = ResolveExecutablePath();
                    if (string.IsNullOrWhiteSpace(exePath))
                    {
                        throw new InvalidOperationException("Unable to determine current executable path for startup registration.");
                    }

                    string command = BuildAutoStartCommand(exePath);
                    key.SetValue(AutoStartValueName, command, RegistryValueKind.String);
                    AppLogger.Info($"Updated Windows startup registration: {AutoStartValueName} = {command}");
                }
                else
                {
                    key.DeleteValue(AutoStartValueName, false);
                    AppLogger.Info("Removed Windows startup registration.");
                }
            }
            catch (Exception ex)
            {
                AppLogger.Error("Failed to update Windows startup registration.", ex);
                throw new InvalidOperationException($"Failed to update Windows auto-start registry entry: {ex.Message}", ex);
            }
        }

        public bool RepairAutoStartIfStale(bool startWithWindowsPreference)
        {
            if (!startWithWindowsPreference)
            {
                return false;
            }

            try
            {
                if (HasAutoStartRegistryValue() && !IsAutoStartEnabled())
                {
                    SetAutoStart(true);
                    AppLogger.Info("Updated stale Windows startup registry entry to current executable path.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not repair stale Windows startup registry entry: {ex.Message}");
            }

            return false;
        }
    }
}
