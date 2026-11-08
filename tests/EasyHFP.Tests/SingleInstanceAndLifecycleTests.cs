using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using EasyHFP.Models;
using EasyHFP.Services;
using EasyHFP.Tests.Fakes;
using EasyHFP.UI;
using Xunit;

namespace EasyHFP.Tests
{
    public class SingleInstanceAndLifecycleTests : IDisposable
    {
        private readonly string _tempDir;

        public SingleInstanceAndLifecycleTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"EasyHFP_LifecycleTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
        }

        [Fact]
        public void SingleInstance_PreventsDuplicate_AndSignalsPrimaryInstance()
        {
            string prefix = $@"Local\EasyHFP_Test_{Guid.NewGuid():N}";
            using var wakeupSignal = new ManualResetEventSlim(false);

            using (var primary = new SingleInstanceCoordinator(prefix))
            {
                Assert.True(primary.IsPrimaryInstance);
                primary.RegisterWakeupCallback(() => wakeupSignal.Set());

                // Simulate a second process (different OS thread) attempting to start
                bool secondaryIsPrimary = true;
                var secondaryThread = new Thread(() =>
                {
                    using var secondary = new SingleInstanceCoordinator(prefix);
                    secondaryIsPrimary = secondary.IsPrimaryInstance;
                    secondary.SignalExistingInstanceToShow();
                });
                secondaryThread.Start();
                secondaryThread.Join();

                Assert.False(secondaryIsPrimary);

                Assert.True(wakeupSignal.Wait(2000), "Primary instance did not receive wakeup signal from secondary instance.");
            }

            // After primary is disposed, a new instance can become primary
            using (var nextPrimary = new SingleInstanceCoordinator(prefix))
            {
                Assert.True(nextPrimary.IsPrimaryInstance);
            }
        }

        [Fact]
        public void MainForm_StartInTray_RemainsHiddenWithCreatedHandle_AndRestoresAndExitsCleanly()
        {
            string configPath = Path.Combine(_tempDir, "config.json");
            string shortcutPath = Path.Combine(_tempDir, "EasyHFP.lnk");
            var configService = new ConfigurationService(configPath);
            var shortcutService = new StartMenuShortcutService(shortcutPath);
            var config = configService.LoadConfig();
            config.SetupCompleted = true;
            config.MinimizeToTray = true;
            config.ShowNotifications = false;
            config.Hotkey = ""; // Avoid global hotkey conflict during unit test
            configService.SaveConfig(config);

            var fakeBt = new FakeBluetoothService();
            fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Test Headset",
                Mac = "112233445566",
                MacUlong = 0x112233445566,
                Connected = true,
                HfpState = HfpServiceState.Disabled
            });

            using var form = new MainForm(fakeBt, configService, startInTray: true, shortcutService: shortcutService);

            // Simulate WinForms Application.Run attempting to show the form
            form.Show();
            Application.DoEvents();

            // Must remain hidden in tray with a valid window handle
            Assert.False(form.Visible);
            Assert.False(form.ShowInTaskbar);
            Assert.True(form.IsHandleCreated);

            // RestoreFromTray brings window to visible normal state
            form.RestoreFromTray();
            Application.DoEvents();

            Assert.True(form.Visible);
            Assert.True(form.ShowInTaskbar);
            Assert.Equal(FormWindowState.Normal, form.WindowState);

            // User closing with MinimizeToTray=true hides to tray instead of disposing
            form.Close();
            Application.DoEvents();

            Assert.False(form.IsDisposed);
            Assert.False(form.Visible);
            Assert.False(form.ShowInTaskbar);

            // Explicit ExitApplication disposes the form even with MinimizeToTray=true
            form.ExitApplication();
            Application.DoEvents();

            Assert.True(form.IsDisposed);
        }

        [Fact]
        public void AppLogger_WritesDiagnosticsToConfiguredFile()
        {
            string customLog = Path.Combine(_tempDir, "test_easyhfp.log");
            try
            {
                AppLogger.Configure(customLog);
                AppLogger.Info("Lifecycle test info message.");
                AppLogger.Warn("Lifecycle test warning message.");
                AppLogger.Error("Lifecycle test error message.", new InvalidOperationException("Simulated fault"));

                Assert.True(File.Exists(customLog));
                string content = File.ReadAllText(customLog);
                Assert.Contains("[INFO] Lifecycle test info message.", content);
                Assert.Contains("[WARN] Lifecycle test warning message.", content);
                Assert.Contains("[ERROR] Lifecycle test error message. | InvalidOperationException: Simulated fault", content);
            }
            finally
            {
                AppLogger.Configure(null);
            }
        }

        public void Dispose()
        {
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
