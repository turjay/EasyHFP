using System;
using System.Collections.Generic;
using EasyHFP.Models;

namespace EasyHFP.Cli
{
    public enum CliAction
    {
        None = 0,
        Gui,
        Tray,
        Toggle,
        Enable,
        Disable,
        Status,
        List,
        Select,
        Help,
        RadioToggleOnly
    }

    public class ParsedCliOptions
    {
        public CliAction Action { get; set; } = CliAction.None;
        public string? TargetDevice { get; set; }
        public ReconnectMode? ReconnectModeOverride { get; set; }
        public bool Silent { get; set; }
        public bool IsValid { get; set; } = true;
        public string? ErrorMessage { get; set; }
    }

    public static class CliParser
    {
        public static ParsedCliOptions Parse(string[] args)
        {
            var result = new ParsedCliOptions();

            if (args == null || args.Length == 0)
            {
                result.Action = CliAction.Gui;
                return result;
            }

            var detectedActions = new List<(CliAction action, string arg)>();

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg.Equals("--gui", StringComparison.OrdinalIgnoreCase) || arg.Equals("-g", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Gui, arg));
                }
                else if (arg.Equals("--tray", StringComparison.OrdinalIgnoreCase) || arg.Equals("--daemon", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Tray, arg));
                }
                else if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) || arg.Equals("-h", StringComparison.OrdinalIgnoreCase) || arg.Equals("/?", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Help, arg));
                }
                else if (arg.Equals("--list", StringComparison.OrdinalIgnoreCase) || arg.Equals("-l", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.List, arg));
                }
                else if (arg.Equals("--select", StringComparison.OrdinalIgnoreCase) || arg.Equals("--setup", StringComparison.OrdinalIgnoreCase) || arg.Equals("-s", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Select, arg));
                }
                else if (arg.Equals("--status", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Status, arg));
                }
                else if (arg.Equals("--toggle", StringComparison.OrdinalIgnoreCase) || arg.Equals("-t", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Toggle, arg));
                }
                else if (arg.Equals("--on", StringComparison.OrdinalIgnoreCase) || arg.Equals("--enable", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Enable, arg));
                }
                else if (arg.Equals("--off", StringComparison.OrdinalIgnoreCase) || arg.Equals("--disable", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.Disable, arg));
                }
                else if (arg.Equals("--radio-toggle-only", StringComparison.OrdinalIgnoreCase))
                {
                    detectedActions.Add((CliAction.RadioToggleOnly, arg));
                }
                else if (arg.Equals("--silent", StringComparison.OrdinalIgnoreCase))
                {
                    result.Silent = true;
                }
                else if (arg.Equals("--device", StringComparison.OrdinalIgnoreCase) || arg.Equals("-d", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        result.TargetDevice = args[++i];
                    }
                    else
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"Missing required argument value for option '{arg}'.";
                        return result;
                    }
                }
                else if (arg.Equals("--reconnect-mode", StringComparison.OrdinalIgnoreCase) || arg.Equals("-m", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        string modeStr = args[++i];
                        if (ReconnectModeExtensions.TryParseReconnectMode(modeStr, out var parsedMode))
                        {
                            result.ReconnectModeOverride = parsedMode;
                        }
                        else
                        {
                            result.IsValid = false;
                            result.ErrorMessage = $"Invalid reconnect mode '{modeStr}'. Valid options are: auto, pnp, none, radio.";
                            return result;
                        }
                    }
                    else
                    {
                        result.IsValid = false;
                        result.ErrorMessage = $"Missing required argument value for option '{arg}'. Valid options are: auto, pnp, none, radio.";
                        return result;
                    }
                }
                else
                {
                    result.IsValid = false;
                    result.ErrorMessage = $"Unknown or unexpected argument '{arg}'. Run with --help to see supported commands and options.";
                    return result;
                }
            }

            // Check conflicting actions
            if (detectedActions.Count > 1)
            {
                result.IsValid = false;
                result.ErrorMessage = $"Conflicting actions specified: {string.Join(", ", detectedActions.ConvertAll(a => a.arg))}. Only one action can be performed at a time.";
                return result;
            }

            if (detectedActions.Count == 1)
            {
                result.Action = detectedActions[0].action;
            }
            else
            {
                // No action specified, but options were provided
                result.IsValid = false;
                result.ErrorMessage = "No action specified. Options like --device or --reconnect-mode cannot be used alone. Specify an action such as --toggle, --on, --off, or --status, or run without arguments to launch the GUI.";
                return result;
            }

            return result;
        }
    }
}
