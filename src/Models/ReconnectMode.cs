using System;

namespace EasyHFP.Models
{
    public enum ReconnectMode
    {
        Auto = 0,
        Pnp = 1,
        None = 2,
        Radio = 3
    }

    public static class ReconnectModeExtensions
    {
        public static string ToConfigString(this ReconnectMode mode)
        {
            return mode switch
            {
                ReconnectMode.Auto => "auto",
                ReconnectMode.Pnp => "pnp",
                ReconnectMode.None => "none",
                ReconnectMode.Radio => "radio",
                _ => "auto"
            };
        }

        public static bool TryParseReconnectMode(string input, out ReconnectMode mode)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                mode = ReconnectMode.Auto;
                return false;
            }

            switch (input.Trim().ToLowerInvariant())
            {
                case "auto":
                    mode = ReconnectMode.Auto;
                    return true;
                case "pnp":
                    mode = ReconnectMode.Pnp;
                    return true;
                case "none":
                    mode = ReconnectMode.None;
                    return true;
                case "radio":
                    mode = ReconnectMode.Radio;
                    return true;
                default:
                    mode = ReconnectMode.Auto;
                    return false;
            }
        }
    }
}
