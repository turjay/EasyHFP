using System;
using System.IO;
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
        public void DefaultConfig_HasAutoReconnectMode()
        {
            string configPath = Path.Combine(_tempDir, "config.txt");
            var service = new ConfigurationService(configPath);

            var config = service.LoadConfig();

            Assert.Equal("auto", config.ReconnectMode);
            Assert.True(config.ShowNotifications);
            Assert.True(config.MinimizeToTray);
            Assert.Equal("Ctrl+Alt+H", config.Hotkey);
        }

        [Fact]
        public void SaveAndLoad_RoundtripsSuccessfully_AndWritesAtomically()
        {
            string configPath = Path.Combine(_tempDir, "config.txt");
            var service = new ConfigurationService(configPath);

            var original = new ConfigData
            {
                DefaultMac = "AABBCCDDEEFF",
                DefaultName = "My Wireless ANC",
                ReconnectMode = "pnp",
                ShowNotifications = false,
                MinimizeToTray = false,
                Hotkey = "Ctrl+Shift+F12"
            };
            original.KnownDevices["AABBCCDDEEFF"] = "My Wireless ANC";

            service.SaveConfig(original);

            Assert.True(File.Exists(configPath));
            var loaded = service.LoadConfig();

            Assert.Equal("AABBCCDDEEFF", loaded.DefaultMac);
            Assert.Equal("My Wireless ANC", loaded.DefaultName);
            Assert.Equal("pnp", loaded.ReconnectMode);
            Assert.False(loaded.ShowNotifications);
            Assert.False(loaded.MinimizeToTray);
            Assert.Equal("Ctrl+Shift+F12", loaded.Hotkey);
            Assert.Equal("My Wireless ANC", loaded.KnownDevices["AABBCCDDEEFF"]);
        }

        [Fact]
        public void NormalizeMac_RemovesPunctuation_AndUppercase()
        {
            var service = new ConfigurationService(Path.Combine(_tempDir, "config.txt"));
            Assert.Equal("AABBCCDDEEFF", service.NormalizeMac("aa:bb:cc:dd:ee:ff"));
            Assert.Equal("AABBCCDDEEFF", service.NormalizeMac("AA-BB-CC-DD-EE-FF"));
            Assert.Equal("", service.NormalizeMac(""));
        }

        [Fact]
        public void MigrationFromLegacyConfig_CopiesSettingsAndLoadsSuccessfully()
        {
            string legacyPath = Path.Combine(_tempDir, "legacy_config.txt");
            string userConfigPath = Path.Combine(_tempDir, "sub_user", "config.txt");

            File.WriteAllText(legacyPath,
                "MAC=112233445566\n" +
                "NAME=Old Headset\n" +
                "RECONNECT_MODE=pnp\n" +
                "SHOW_NOTIFICATIONS=false\n" +
                "[DEVICES]\n" +
                "112233445566=Old Headset\n");

            var service = new ConfigurationService(userConfigPath, legacyPath);
            var loaded = service.LoadConfig();

            // Verifies migration copied file to new location
            Assert.True(File.Exists(userConfigPath));
            Assert.Equal("112233445566", loaded.DefaultMac);
            Assert.Equal("Old Headset", loaded.DefaultName);
            Assert.Equal("pnp", loaded.ReconnectMode);
            Assert.False(loaded.ShowNotifications);
        }

        [Fact]
        public void CorruptedConfigValues_FallbackToSafeDefaults()
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
        public void AutoStartRegistry_EnableAndDisable_WorksAndValidatesTargetFile()
        {
            // Create a dummy executable file in a path with spaces
            string spacedDir = Path.Combine(_tempDir, "Dir With Spaces");
            Directory.CreateDirectory(spacedDir);
            string fakeExePath = Path.Combine(spacedDir, "EasyHFP.exe");
            File.WriteAllText(fakeExePath, "dummy");

            string configPath = Path.Combine(_tempDir, "config.txt");
            var service = new ConfigurationService(configPath, null, _testRegistrySubKey, fakeExePath);

            Assert.False(service.IsAutoStartEnabled());

            service.SetAutoStart(true);
            Assert.True(service.IsAutoStartEnabled());

            // Verify no duplicate or unquoted command was written
            using (var key = Registry.CurrentUser.OpenSubKey(_testRegistrySubKey, false))
            {
                Assert.NotNull(key);
                string? regVal = key!.GetValue(ConfigurationService.AutoStartValueName) as string;
                Assert.Equal($"\"{fakeExePath}\" --tray", regVal);
            }

            // If target executable does not match current executable path, IsAutoStartEnabled returns false
            string movedExePath = Path.Combine(spacedDir, "OtherLocation", "EasyHFP.exe");
            var movedService = new ConfigurationService(configPath, null, _testRegistrySubKey, movedExePath);
            Assert.False(movedService.IsAutoStartEnabled());

            // Disable auto-start removes the registry value
            service.SetAutoStart(false);
            Assert.False(service.IsAutoStartEnabled());
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
