using System;
using System.Collections.Generic;

namespace EasyHFP.Models
{
    public class ConfigData
    {
        public string DefaultMac { get; set; } = string.Empty;
        public string DefaultName { get; set; } = "Bluetooth Headset";
        public string ReconnectMode { get; set; } = "auto";
        public bool ShowNotifications { get; set; } = true;
        public bool MinimizeToTray { get; set; } = true;
        public string Hotkey { get; set; } = "Ctrl+Alt+H";
        public Dictionary<string, string> KnownDevices { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public ConfigData Clone()
        {
            var clone = new ConfigData
            {
                DefaultMac = DefaultMac,
                DefaultName = DefaultName,
                ReconnectMode = ReconnectMode,
                ShowNotifications = ShowNotifications,
                MinimizeToTray = MinimizeToTray,
                Hotkey = Hotkey,
                KnownDevices = new Dictionary<string, string>(KnownDevices, StringComparer.OrdinalIgnoreCase)
            };
            return clone;
        }
    }
}
