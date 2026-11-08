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
            var shortcutService = new StartMenuShortcutService();
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

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                using var profileController = new WindowsAudioProfileController();
                var config = configService.LoadConfig();

                OnboardingWizardForm? activeOnboarding = null;
                MainForm? activeMainForm = null;

                singleInstance.RegisterWakeupCallback(() =>
                {
                    if (activeOnboarding != null && !activeOnboarding.IsDisposed)
                    {
                        activeOnboarding.BringToFrontFromSecondaryInstance();
                    }
                    else if (activeMainForm != null && !activeMainForm.IsDisposed)
                    {
                        activeMainForm.RestoreFromTray();
                    }
                });

                bool startInTray = (parsed.Action == CliAction.Tray);

                if (!config.SetupCompleted)
                {
                    AppLogger.Info("First-run or uncompleted configuration detected; launching onboarding setup wizard.");
                    using (var wizard = new OnboardingWizardForm(bluetoothService, configService, shortcutService, profileController, config))
                    {
                        activeOnboarding = wizard;
                        var dialogResult = wizard.ShowDialog();
                        activeOnboarding = null;

                        if (dialogResult != DialogResult.OK)
                        {
                            AppLogger.Info("Onboarding wizard was cancelled; exiting without marking setup complete.");
                            return 0;
                        }
                    }

                    // Reload saved config and open the main window visibly right after onboarding completes
                    config = configService.LoadConfig();
                    startInTray = false;
                }
                else
                {
                    // Portable path maintenance: update stale Start Menu shortcut or Startup registry entry if moved
                    shortcutService.RepairShortcutIfStale(config.AddToStartMenu);
                    configService.RepairAutoStartIfStale(config.StartWithWindows);
                }

                AppLogger.Info($"Starting EasyHFP in {(startInTray ? "Tray" : "GUI")} mode.");

                using var mainForm = new MainForm(bluetoothService, configService, startInTray, profileController, shortcutService);
                activeMainForm = mainForm;
                Application.Run(mainForm);
                activeMainForm = null;

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
