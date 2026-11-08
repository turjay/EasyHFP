using System;
using System.IO;
using System.Text.Json;
using EasyHFP.Models;
using EasyHFP.Services;
using Microsoft.Win32;
using Xunit;

namespace EasyHFP.Tests
{
    public class ConfigurationServiceTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _testRegistrySubKey;

        public ConfigurationServiceTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"EasyHFP_TestConfig_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _testRegistrySubKey = $@"Software\EasyHFP_UnitTests_{Guid.NewGuid():N}";
        }

        [Fact]
        public void FirstRunDetection_WhenNoConfigExists_ReturnsUncompletedWithExpectedDefaults()
        {
            string configPath = Path.Combine(_tempDir, "config.json");
            var service = new ConfigurationService(configPath);

            var config = service.LoadConfig();

            Assert.Equal(1, config.SchemaVersion);
            Assert.False(config.SetupCompleted);
            Assert.Equal(string.Empty, config.SelectedDeviceId);
            Assert.Equal("auto", config.ReconnectMode);
            Assert.True(config.AddToStartMenu);
            Assert.False(config.StartWithWindows);
            Assert.True(config.MinimizeToTray);
            Assert.True(config.ShowNotifications);
            Assert.Equal("Ctrl+Alt+H", config.Hotkey);
        }

        [Fact]
        public void SaveAndLoad_RoundtripsJsonSchema_AndWritesAtomically()
        {
            string configPath = Path.Combine(_tempDir, "config.json");
            var service = new ConfigurationService(configPath);

            var original = new ConfigData
            {
                SetupCompleted = true,
                DefaultMac = "aa:bb:cc:dd:ee:ff",
                DefaultName = "My Wireless ANC",
                ReconnectMode = "pnp",
                AddToStartMenu = true,
                StartWithWindows = true,
                ShowNotifications = false,
                MinimizeToTray = false,
                Hotkey = "Ctrl+Shift+F12"
            };
            original.KnownDevices["AABBCCDDEEFF"] = "My Wireless ANC";

            service.SaveConfig(original);

            Assert.True(File.Exists(configPath));
            Assert.Empty(Directory.GetFiles(_tempDir, "*.tmp*"));

            string rawJson = File.ReadAllText(configPath);
            using (JsonDocument doc = JsonDocument.Parse(rawJson))
            {
                var root = doc.RootElement;
                Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
                Assert.True(root.GetProperty("setupCompleted").GetBoolean());
                Assert.Equal("AABBCCDDEEFF", root.GetProperty("selectedDeviceId").GetString());
                Assert.Equal("My Wireless ANC", root.GetProperty("selectedDeviceName").GetString());

                var prefs = root.GetProperty("preferences");
                Assert.True(prefs.GetProperty("startWithWindows").GetBoolean());
                Assert.True(prefs.GetProperty("addToStartMenu").GetBoolean());
                Assert.False(prefs.GetProperty("closeToTray").GetBoolean());
                Assert.False(prefs.GetProperty("notificationsEnabled").GetBoolean());
            }

            var loaded = service.LoadConfig();

            Assert.True(loaded.SetupCompleted);
            Assert.Equal("AABBCCDDEEFF", loaded.DefaultMac);
            Assert.Equal("My Wireless ANC", loaded.DefaultName);
            Assert.Equal("pnp", loaded.ReconnectMode);
            Assert.True(loaded.AddToStartMenu);
            Assert.True(loaded.StartWithWindows);
            Assert.False(loaded.ShowNotifications);
            Assert.False(loaded.MinimizeToTray);
            Assert.Equal("Ctrl+Shift+F12", loaded.Hotkey);
            Assert.Equal("My Wireless ANC", loaded.KnownDevices["AABBCCDDEEFF"]);
        }

        [Fact]
        public void NormalizeMac_RemovesPunctuation_AndUppercase()
        {
            var service = new ConfigurationService(Path.Combine(_tempDir, "config.json"));
            Assert.Equal("AABBCCDDEEFF", service.NormalizeMac("aa:bb:cc:dd:ee:ff"));
            Assert.Equal("AABBCCDDEEFF", service.NormalizeMac("AA-BB-CC-DD-EE-FF"));
            Assert.Equal("", service.NormalizeMac(""));
        }

        [Fact]
        public void MigrationFromLegacyConfigTxt_WithValidDevice_MarksSetupCompletedAndPreservesOriginalFile()
        {
            string legacyPath = Path.Combine(_tempDir, "config.txt");
            string jsonConfigPath = Path.Combine(_tempDir, "config.json");

            File.WriteAllText(legacyPath,
                "MAC=112233445566\n" +
                "NAME=Old Headset\n" +
                "RECONNECT_MODE=pnp\n" +
                "SHOW_NOTIFICATIONS=false\n" +
                "MINIMIZE_TO_TRAY=false\n" +
                "[DEVICES]\n" +
                "112233445566=Old Headset\n");

            var service = new ConfigurationService(jsonConfigPath, legacyPath);
            var loaded = service.LoadConfig();

            // Both the new config.json and preserved original config.txt must exist
            Assert.True(File.Exists(jsonConfigPath));
            Assert.True(File.Exists(legacyPath));

            // Because a valid MAC was configured in config.txt, setupCompleted is true
            Assert.True(loaded.SetupCompleted);
            Assert.Equal("112233445566", loaded.DefaultMac);
            Assert.Equal("Old Headset", loaded.DefaultName);
            Assert.Equal("pnp", loaded.ReconnectMode);
            Assert.False(loaded.ShowNotifications);
            Assert.False(loaded.MinimizeToTray);
        }

        [Fact]
        public void MigrationFromIncompleteLegacyConfigTxt_WithoutSelectedDevice_LeavesSetupCompletedFalseForOnboardingReview()
        {
            string legacyPath = Path.Combine(_tempDir, "incomplete_config.txt");
            string jsonConfigPath = Path.Combine(_tempDir, "config.json");

            File.WriteAllText(legacyPath,
                "MAC=\n" +
                "SHOW_NOTIFICATIONS=false\n" +
                "MINIMIZE_TO_TRAY=true\n");

            var service = new ConfigurationService(jsonConfigPath, legacyPath);
            var loaded = service.LoadConfig();

            Assert.True(File.Exists(jsonConfigPath));
            Assert.True(File.Exists(legacyPath));
            Assert.False(loaded.SetupCompleted);
            Assert.Equal(string.Empty, loaded.DefaultMac);
            Assert.False(loaded.ShowNotifications);
            Assert.True(loaded.MinimizeToTray);
        }

        [Fact]
        public void MalformedJsonConfig_DoesNotCrash_AndFallsBackToSafeUncompletedDefaults()
        {
            string jsonConfigPath = Path.Combine(_tempDir, "config.json");
            File.WriteAllText(jsonConfigPath, "{ \"schemaVersion\": 1, \"setupCompleted\": true, BROKEN_JSON ");

            var service = new ConfigurationService(jsonConfigPath);
            var loaded = service.LoadConfig();

            Assert.False(loaded.SetupCompleted);
            Assert.Equal(string.Empty, loaded.DefaultMac);
            Assert.Equal("auto", loaded.ReconnectMode);
            Assert.True(loaded.AddToStartMenu);
            Assert.False(loaded.StartWithWindows);
            Assert.True(loaded.MinimizeToTray);
            Assert.True(loaded.ShowNotifications);
            Assert.Equal("Ctrl+Alt+H", loaded.Hotkey);
        }

        [Fact]
        public void CorruptedLegacyConfigValues_FallbackToSafeDefaults()
        {
            string configPath = Path.Combine(_tempDir, "corrupted_config.txt");
            File.WriteAllText(configPath,
                "MAC=NOT_A_VALID_MAC\n" +
                "RECONNECT_MODE=invalid_mode\n" +
                "SHOW_NOTIFICATIONS=not_a_bool\n" +
                "MINIMIZE_TO_TRAY=not_a_bool\n" +
                "HOTKEY=   \n" +
                "[DEVICES]\n" +
                "BAD_MAC=Broken Device\n" +
                "AABBCCDDEEFF=Valid Device\n");

            var service = new ConfigurationService(configPath);
            var loaded = service.LoadConfig();

            Assert.Equal(string.Empty, loaded.DefaultMac);
            Assert.Equal("auto", loaded.ReconnectMode);
            Assert.True(loaded.ShowNotifications);
            Assert.True(loaded.MinimizeToTray);
            Assert.Equal("Ctrl+Alt+H", loaded.Hotkey);
            Assert.False(loaded.KnownDevices.ContainsKey("BAD_MAC"));
            Assert.True(loaded.KnownDevices.ContainsKey("AABBCCDDEEFF"));
        }

        [Fact]
        public void AutoStartCommand_HandlesPathsWithSpaces()
        {
            string pathWithSpaces = @"C:\Program Files\EasyHFP Utility\EasyHFP.exe";
            string cmd = ConfigurationService.BuildAutoStartCommand(pathWithSpaces);

            Assert.Equal("\"C:\\Program Files\\EasyHFP Utility\\EasyHFP.exe\" --tray", cmd);
            Assert.True(ConfigurationService.TryExtractExecutablePath(cmd, out string extracted));
            Assert.Equal(pathWithSpaces, extracted);
        }

        [Fact]
        public void AutoStartRegistry_EnableDisableAndStalePathRepair_WorksCorrectly()
        {
            string spacedDir = Path.Combine(_tempDir, "Dir With Spaces");
            Directory.CreateDirectory(spacedDir);
            string fakeExePath = Path.Combine(spacedDir, "EasyHFP.exe");
            File.WriteAllText(fakeExePath, "dummy");

            string configPath = Path.Combine(_tempDir, "config.json");
            var service = new ConfigurationService(configPath, null, _testRegistrySubKey, fakeExePath);

            Assert.False(service.IsAutoStartEnabled());

            service.SetAutoStart(true);
            Assert.True(service.IsAutoStartEnabled());

            using (var key = Registry.CurrentUser.OpenSubKey(_testRegistrySubKey, false))
            {
                Assert.NotNull(key);
                string? regVal = key!.GetValue(ConfigurationService.AutoStartValueName) as string;
                Assert.Equal($"\"{fakeExePath}\" --tray", regVal);
            }

            // Simulate moving portable EasyHFP.exe to a new folder
            string movedDir = Path.Combine(_tempDir, "Moved Location");
            Directory.CreateDirectory(movedDir);
            string movedExePath = Path.Combine(movedDir, "EasyHFP.exe");
            File.WriteAllText(movedExePath, "dummy");

            var movedService = new ConfigurationService(configPath, null, _testRegistrySubKey, movedExePath);
            Assert.False(movedService.IsAutoStartEnabled());

            // When startWithWindowsPreference is false, do not repair
            Assert.False(movedService.RepairAutoStartIfStale(startWithWindowsPreference: false));
            Assert.False(movedService.IsAutoStartEnabled());

            // When startWithWindowsPreference is true, repair updates the registry entry to the new path
            Assert.True(movedService.RepairAutoStartIfStale(startWithWindowsPreference: true));
            Assert.True(movedService.IsAutoStartEnabled());

            using (var key = Registry.CurrentUser.OpenSubKey(_testRegistrySubKey, false))
            {
                string? regVal = key!.GetValue(ConfigurationService.AutoStartValueName) as string;
                Assert.Equal($"\"{movedExePath}\" --tray", regVal);
            }

            movedService.SetAutoStart(false);
            Assert.False(movedService.IsAutoStartEnabled());
        }

        public void Dispose()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(_testRegistrySubKey, false);
            }
            catch { }

            try
            {
                if (Directory.Exists(_tempDir))
                {
                    Directory.Delete(_tempDir, true);
                }
            }
            catch { }
        }
    }
}
