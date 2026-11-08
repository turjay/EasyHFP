using System;
using System.Windows.Forms;
using EasyHFP.Cli;
using EasyHFP.Interop;
using EasyHFP.Services;
using EasyHFP.UI;

namespace EasyHFP
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(AppContext.BaseDirectory))
                {
                    Environment.CurrentDirectory = AppContext.BaseDirectory;
                }
            }
            catch
            {
                // Ignore working directory normalization errors
            }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) =>
            {
                AppLogger.Error("Unhandled UI thread exception.", e.Exception);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                AppLogger.Error("Unhandled domain exception.", e.ExceptionObject as Exception);
            };

            NativeUser32.EnableHighDpiAwareness();

            var parsed = CliParser.Parse(args);

            var configService = new ConfigurationService();
            var bluetoothService = new BluetoothService();

            if (parsed.Action == CliAction.Gui || parsed.Action == CliAction.Tray)
            {
                using var singleInstance = new SingleInstanceCoordinator();
                if (!singleInstance.IsPrimaryInstance)
                {
                    if (parsed.Action == CliAction.Gui)
                    {
                        AppLogger.Info("Secondary GUI launch detected; signaling running instance to show window.");
                        singleInstance.SignalExistingInstanceToShow();
                    }
                    else
                    {
                        AppLogger.Info("Secondary tray launch detected; an instance is already running.");
                    }
                    return 0;
                }

                bool startInTray = (parsed.Action == CliAction.Tray);
                AppLogger.Info($"Starting EasyHFP in {(startInTray ? "Tray" : "GUI")} mode.");

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using var profileController = new WindowsAudioProfileController();
                using var mainForm = new MainForm(bluetoothService, configService, startInTray, profileController);
                singleInstance.RegisterWakeupCallback(() => mainForm.RestoreFromTray());
                Application.Run(mainForm);

                profileController.ReleaseAllHolds();
                AppLogger.Info("EasyHFP exited cleanly.");
                return 0;
            }

            // Console Execution
            NativeUser32.TryAttachParentConsole();
            var runner = new CliRunner(bluetoothService, configService);
            int exitCode = runner.Run(parsed);
            return exitCode;
        }
    }
}
