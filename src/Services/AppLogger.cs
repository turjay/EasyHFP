using System;
using System.IO;
using System.Text;

namespace EasyHFP.Services
{
    /// <summary>
    /// Lightweight, bounded, thread-safe file logger for startup, lifecycle, configuration,
    /// and Bluetooth transition diagnostics.
    /// </summary>
    public static class AppLogger
    {
        private const long MaxLogSizeBytes = 512 * 1024; // 512 KB
        private static readonly object _logLock = new();
        private static string _logFilePath = GetDefaultLogPath();

        public static string LogFilePath
        {
            get
            {
                lock (_logLock)
                {
                    return _logFilePath;
                }
            }
        }

        public static void Configure(string? customLogFilePath = null)
        {
            lock (_logLock)
            {
                _logFilePath = string.IsNullOrWhiteSpace(customLogFilePath)
                    ? GetDefaultLogPath()
                    : customLogFilePath;
            }
        }

        private static string GetDefaultLogPath()
        {
            try
            {
                string processName = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
                if (processName.StartsWith("testhost", StringComparison.OrdinalIgnoreCase) ||
                    processName.StartsWith("vstest", StringComparison.OrdinalIgnoreCase))
                {
                    return Path.Combine(Path.GetTempPath(), "EasyHFP_Tests", "easyhfp_test.log");
                }

                string? envAppDataDir = Environment.GetEnvironmentVariable("EASYHFP_APPDATA_DIR");
                string appDataDir = !string.IsNullOrWhiteSpace(envAppDataDir)
                    ? envAppDataDir
                    : Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                        "EasyHFP");
                return Path.Combine(appDataDir, "easyhfp.log");
            }
            catch
            {
                return Path.Combine(AppContext.BaseDirectory, "easyhfp.log");
            }
        }

        public static void Info(string message) => Write("INFO", message);

        public static void Warn(string message) => Write("WARN", message);

        public static void Error(string message, Exception? ex = null)
        {
            if (ex != null)
            {
                Write("ERROR", $"{message} | {ex.GetType().Name}: {ex.Message}");
            }
            else
            {
                Write("ERROR", message);
            }
        }

        private static void Write(string level, string message)
        {
            try
            {
                lock (_logLock)
                {
                    string? dir = Path.GetDirectoryName(_logFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    RotateIfNeeded(_logFilePath);

                    string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [PID:{Environment.ProcessId}] [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, line, Encoding.UTF8);
                }
            }
            catch
            {
                // Diagnostics must never crash the application.
            }
        }

        private static void RotateIfNeeded(string path)
        {
            try
            {
                if (!File.Exists(path)) return;
                var info = new FileInfo(path);
                if (info.Length >= MaxLogSizeBytes)
                {
                    string archivePath = path + ".old";
                    File.Move(path, archivePath, overwrite: true);
                }
            }
            catch
            {
                // Ignore rotation errors
            }
        }
    }
}
