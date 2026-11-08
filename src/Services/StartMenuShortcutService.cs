using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using EasyHFP.Interop;

namespace EasyHFP.Services
{
    public interface IStartMenuShortcutService
    {
        string ShortcutFilePath { get; }
        bool IsShortcutPresent();
        bool IsShortcutValidForCurrentExecutable();
        string? TryGetShortcutTargetPath();
        string? TryGetShortcutWorkingDirectory();
        void SetShortcutEnabled(bool enable);
        bool RepairShortcutIfStale(bool addToStartMenuPreference);
    }

    public class StartMenuShortcutService : IStartMenuShortcutService
    {
        public const string ShortcutFileName = "EasyHFP.lnk";
        public const string ShortcutDescription = "EasyHFP — Bluetooth Audio Profile Switcher (A2DP / HFP)";

        private readonly string _shortcutFilePath;
        private readonly string? _customExecutablePath;

        public string ShortcutFilePath => _shortcutFilePath;

        public StartMenuShortcutService(string? customShortcutFilePath = null, string? customExecutablePath = null)
        {
            if (!string.IsNullOrWhiteSpace(customShortcutFilePath))
            {
                _shortcutFilePath = customShortcutFilePath;
            }
            else
            {
                string programsDir = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                if (string.IsNullOrWhiteSpace(programsDir))
                {
                    string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                    programsDir = Path.Combine(appData, "Microsoft", "Windows", "Start Menu", "Programs");
                }
                _shortcutFilePath = Path.Combine(programsDir, ShortcutFileName);
            }

            _customExecutablePath = customExecutablePath;
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

        public bool IsShortcutPresent()
        {
            return !string.IsNullOrWhiteSpace(_shortcutFilePath) && File.Exists(_shortcutFilePath);
        }

        public string? TryGetShortcutTargetPath()
        {
            if (!IsShortcutPresent()) return null;

            object? comObj = null;
            try
            {
                var shellLinkType = Type.GetTypeFromCLSID(NativeShellLink.ClsidShellLink, throwOnError: false);
                if (shellLinkType == null) return null;

                comObj = Activator.CreateInstance(shellLinkType);
                if (comObj is not NativeShellLink.IShellLinkW shellLink || comObj is not IPersistFile persistFile)
                {
                    return null;
                }

                persistFile.Load(_shortcutFilePath, 0);
                var sb = new StringBuilder(1024);
                int hr = shellLink.GetPath(sb, sb.Capacity, IntPtr.Zero, NativeShellLink.SLGP_RAWPATH);
                if (hr < 0) return null;

                string path = sb.ToString().Trim();
                return string.IsNullOrEmpty(path) ? null : path;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read Start Menu shortcut target from '{_shortcutFilePath}': {ex.Message}");
                return null;
            }
            finally
            {
                if (comObj != null && Marshal.IsComObject(comObj))
                {
                    try { Marshal.FinalReleaseComObject(comObj); } catch { }
                }
            }
        }

        public string? TryGetShortcutWorkingDirectory()
        {
            if (!IsShortcutPresent()) return null;

            object? comObj = null;
            try
            {
                var shellLinkType = Type.GetTypeFromCLSID(NativeShellLink.ClsidShellLink, throwOnError: false);
                if (shellLinkType == null) return null;

                comObj = Activator.CreateInstance(shellLinkType);
                if (comObj is not NativeShellLink.IShellLinkW shellLink || comObj is not IPersistFile persistFile)
                {
                    return null;
                }

                persistFile.Load(_shortcutFilePath, 0);
                var sb = new StringBuilder(1024);
                int hr = shellLink.GetWorkingDirectory(sb, sb.Capacity);
                if (hr < 0) return null;

                string dir = sb.ToString().Trim();
                return string.IsNullOrEmpty(dir) ? null : dir;
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Failed to read Start Menu shortcut working directory from '{_shortcutFilePath}': {ex.Message}");
                return null;
            }
            finally
            {
                if (comObj != null && Marshal.IsComObject(comObj))
                {
                    try { Marshal.FinalReleaseComObject(comObj); } catch { }
                }
            }
        }

        public bool IsShortcutValidForCurrentExecutable()
        {
            if (!IsShortcutPresent()) return false;

            string? targetPath = TryGetShortcutTargetPath();
            if (string.IsNullOrWhiteSpace(targetPath)) return false;

            if (!File.Exists(targetPath)) return false;

            string? currentExePath = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(currentExePath)) return true;

            try
            {
                string currentFileName = Path.GetFileName(currentExePath);
                if (!string.IsNullOrWhiteSpace(_customExecutablePath) ||
                    currentFileName.Equals("EasyHFP.exe", StringComparison.OrdinalIgnoreCase))
                {
                    bool targetMatches = string.Equals(
                        Path.GetFullPath(targetPath),
                        Path.GetFullPath(currentExePath),
                        StringComparison.OrdinalIgnoreCase);

                    if (!targetMatches) return false;

                    string? expectedWorkDir = Path.GetDirectoryName(Path.GetFullPath(currentExePath));
                    string? actualWorkDir = TryGetShortcutWorkingDirectory();
                    if (!string.IsNullOrWhiteSpace(expectedWorkDir) && !string.IsNullOrWhiteSpace(actualWorkDir))
                    {
                        return string.Equals(
                            Path.GetFullPath(actualWorkDir),
                            Path.GetFullPath(expectedWorkDir),
                            StringComparison.OrdinalIgnoreCase);
                    }

                    return true;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        public void SetShortcutEnabled(bool enable)
        {
            if (!enable)
            {
                try
                {
                    if (File.Exists(_shortcutFilePath))
                    {
                        File.Delete(_shortcutFilePath);
                        AppLogger.Info($"Removed Start Menu shortcut at '{_shortcutFilePath}'.");
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Error($"Failed to remove Start Menu shortcut at '{_shortcutFilePath}'.", ex);
                    throw new InvalidOperationException($"Failed to remove Start Menu shortcut: {ex.Message}", ex);
                }
                return;
            }

            string? exePath = ResolveExecutablePath();
            if (string.IsNullOrWhiteSpace(exePath))
            {
                throw new InvalidOperationException("Unable to determine current executable path for Start Menu shortcut.");
            }

            string fullExePath = Path.GetFullPath(exePath);
            string workingDirectory = Path.GetDirectoryName(fullExePath) ?? AppContext.BaseDirectory;

            object? comObj = null;
            try
            {
                string? shortcutDir = Path.GetDirectoryName(_shortcutFilePath);
                if (!string.IsNullOrWhiteSpace(shortcutDir) && !Directory.Exists(shortcutDir))
                {
                    Directory.CreateDirectory(shortcutDir);
                }

                var shellLinkType = Type.GetTypeFromCLSID(NativeShellLink.ClsidShellLink, throwOnError: true)!;
                comObj = Activator.CreateInstance(shellLinkType);

                if (comObj is not NativeShellLink.IShellLinkW shellLink || comObj is not IPersistFile persistFile)
                {
                    throw new InvalidOperationException("Windows Shell Link COM interface is unavailable.");
                }

                Marshal.ThrowExceptionForHR(shellLink.SetPath(fullExePath));
                Marshal.ThrowExceptionForHR(shellLink.SetWorkingDirectory(workingDirectory));
                Marshal.ThrowExceptionForHR(shellLink.SetDescription(ShortcutDescription));
                Marshal.ThrowExceptionForHR(shellLink.SetIconLocation(fullExePath, 0));
                Marshal.ThrowExceptionForHR(shellLink.SetShowCmd(NativeShellLink.SW_SHOWNORMAL));

                persistFile.Save(_shortcutFilePath, true);
                AppLogger.Info($"Created or updated Start Menu shortcut at '{_shortcutFilePath}' -> '{fullExePath}'.");
            }
            catch (Exception ex)
            {
                AppLogger.Error($"Failed to create or update Start Menu shortcut at '{_shortcutFilePath}'.", ex);
                throw new InvalidOperationException($"Failed to create Start Menu shortcut: {ex.Message}", ex);
            }
            finally
            {
                if (comObj != null && Marshal.IsComObject(comObj))
                {
                    try { Marshal.FinalReleaseComObject(comObj); } catch { }
                }
            }
        }

        public bool RepairShortcutIfStale(bool addToStartMenuPreference)
        {
            if (!addToStartMenuPreference)
            {
                return false;
            }

            try
            {
                if (IsShortcutPresent() && !IsShortcutValidForCurrentExecutable())
                {
                    SetShortcutEnabled(true);
                    AppLogger.Info("Updated stale Start Menu shortcut to current executable path.");
                    return true;
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn($"Could not repair stale Start Menu shortcut: {ex.Message}");
            }

            return false;
        }
    }
}
