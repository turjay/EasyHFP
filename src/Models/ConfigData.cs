using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace EasyHFP.Models
{
    public sealed class UserPreferences
    {
        [JsonPropertyName("startWithWindows")]
        public bool StartWithWindows { get; set; } = false;

        [JsonPropertyName("addToStartMenu")]
        public bool AddToStartMenu { get; set; } = true;

        [JsonPropertyName("closeToTray")]
        public bool CloseToTray { get; set; } = true;

        [JsonPropertyName("notificationsEnabled")]
        public bool NotificationsEnabled { get; set; } = true;

        public UserPreferences Clone()
        {
            return new UserPreferences
            {
                StartWithWindows = StartWithWindows,
                AddToStartMenu = AddToStartMenu,
                CloseToTray = CloseToTray,
                NotificationsEnabled = NotificationsEnabled
            };
        }
    }

    public class ConfigData
    {
        public const int CurrentSchemaVersion = 1;

        private UserPreferences _preferences = new();

        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CurrentSchemaVersion;

        [JsonPropertyName("setupCompleted")]
        public bool SetupCompleted { get; set; } = false;

        [JsonPropertyName("selectedDeviceId")]
        public string SelectedDeviceId { get; set; } = string.Empty;

        [JsonPropertyName("selectedDeviceName")]
        public string SelectedDeviceName { get; set; } = "Bluetooth Headset";

        [JsonPropertyName("reconnectMode")]
        public string ReconnectMode { get; set; } = "auto";

        [JsonPropertyName("hotkey")]
        public string Hotkey { get; set; } = "Ctrl+Alt+H";

        [JsonPropertyName("knownDevices")]
        public Dictionary<string, string> KnownDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [JsonPropertyName("preferences")]
        public UserPreferences Preferences
        {
            get => _preferences;
            set => _preferences = value ?? new UserPreferences();
        }

        [JsonIgnore]
        public string DefaultMac
        {
            get => SelectedDeviceId;
            set => SelectedDeviceId = value ?? string.Empty;
        }

        [JsonIgnore]
        public string DefaultName
        {
            get => SelectedDeviceName;
            set => SelectedDeviceName = value ?? "Bluetooth Headset";
        }

        [JsonIgnore]
        public bool ShowNotifications
        {
            get => Preferences.NotificationsEnabled;
            set => Preferences.NotificationsEnabled = value;
        }

        [JsonIgnore]
        public bool MinimizeToTray
        {
            get => Preferences.CloseToTray;
            set => Preferences.CloseToTray = value;
        }

        [JsonIgnore]
        public bool StartWithWindows
        {
            get => Preferences.StartWithWindows;
            set => Preferences.StartWithWindows = value;
        }

        [JsonIgnore]
        public bool AddToStartMenu
        {
            get => Preferences.AddToStartMenu;
            set => Preferences.AddToStartMenu = value;
        }

        public ConfigData Clone()
        {
            var clone = new ConfigData
            {
                SchemaVersion = SchemaVersion,
                SetupCompleted = SetupCompleted,
                SelectedDeviceId = SelectedDeviceId,
                SelectedDeviceName = SelectedDeviceName,
                ReconnectMode = ReconnectMode,
                Hotkey = Hotkey,
                Preferences = Preferences.Clone(),
                KnownDevices = new Dictionary<string, string>(KnownDevices, StringComparer.OrdinalIgnoreCase)
            };
            return clone;
        }
    }
}
