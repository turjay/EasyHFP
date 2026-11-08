using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using EasyHFP.Models;
using EasyHFP.Services;
using EasyHFP.Tests.Fakes;
using EasyHFP.UI;
using Microsoft.Win32;
using Xunit;

namespace EasyHFP.Tests
{
    public class OnboardingAndIntegrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _testRegistrySubKey;

        public OnboardingAndIntegrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"EasyHFP_OnboardingTest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _testRegistrySubKey = $@"Software\EasyHFP_OnboardingTests_{Guid.NewGuid():N}";
        }

        private sealed class SpyAudioProfileController : IAudioProfileController
        {
            public int QuerySnapshotCount { get; private set; }
            public int SwitchToHfpCount { get; private set; }
            public int SwitchToA2dpCount { get; private set; }
            public int ToggleProfileCount { get; private set; }
            public bool IsHoldingHfpStream => false;
            public string? ActiveHoldDeviceMac => null;

            public bool SimulateA2dpEndpoint { get; set; } = true;
            public bool SimulateHfpEndpoint { get; set; } = true;

#pragma warning disable CS0067
            public event Action? ProfileStateChanged;
#pragma warning restore CS0067

            public HeadsetProfileSnapshot QueryProfileSnapshot(BtDeviceItem device)
            {
                QuerySnapshotCount++;
                return new HeadsetProfileSnapshot
                {
                    DeviceName = device.Name,
                    BluetoothMac = device.Mac,
                    IsBluetoothConnected = device.Connected,
                    IsRenderEndpointActive = SimulateA2dpEndpoint,
                    IsCaptureEndpointActive = SimulateHfpEndpoint,
                    InferredMode = device.Connected ? AudioProfileMode.A2dp : AudioProfileMode.Disconnected
                };
            }

            public Task<ProfileTransitionResult> SwitchToHfpAsync(BtDeviceItem device, CancellationToken cancellationToken = default)
            {
                SwitchToHfpCount++;
                return Task.FromResult(new ProfileTransitionResult { Success = true });
            }

            public Task<ProfileTransitionResult> SwitchToA2dpAsync(BtDeviceItem device, CancellationToken cancellationToken = default)
            {
                SwitchToA2dpCount++;
                return Task.FromResult(new ProfileTransitionResult { Success = true });
            }

            public Task<ProfileTransitionResult> ToggleProfileAsync(BtDeviceItem device, CancellationToken cancellationToken = default)
            {
                ToggleProfileCount++;
                return Task.FromResult(new ProfileTransitionResult { Success = true });
            }

            public void ReleaseAllHolds()
            {
            }

            public void Dispose()
            {
            }
        }

        [Fact]
        public void StartMenuShortcut_CreateVerifyRepairAndRemove_WorksWithoutDuplicates()
        {
            string appDir1 = Path.Combine(_tempDir, "Portable Folder One");
            Directory.CreateDirectory(appDir1);
            string exePath1 = Path.Combine(appDir1, "EasyHFP.exe");
            File.WriteAllText(exePath1, "dummy-binary-1");

            string programsDir = Path.Combine(_tempDir, "Start Menu", "Programs");
            string shortcutPath = Path.Combine(programsDir, "EasyHFP.lnk");

            var shortcutService1 = new StartMenuShortcutService(shortcutPath, exePath1);

            Assert.False(shortcutService1.IsShortcutPresent());
            Assert.False(shortcutService1.IsShortcutValidForCurrentExecutable());

            // 1. Create shortcut
            shortcutService1.SetShortcutEnabled(true);

            Assert.True(shortcutService1.IsShortcutPresent());
            Assert.True(shortcutService1.IsShortcutValidForCurrentExecutable());
            Assert.Equal(Path.GetFullPath(exePath1), Path.GetFullPath(shortcutService1.TryGetShortcutTargetPath()!), ignoreCase: true);
            Assert.Equal(Path.GetFullPath(appDir1), Path.GetFullPath(shortcutService1.TryGetShortcutWorkingDirectory()!), ignoreCase: true);
            Assert.Single(Directory.GetFiles(programsDir, "*.lnk"));

            // 2. Simulate moving portable EasyHFP.exe to a new folder
            string appDir2 = Path.Combine(_tempDir, "Portable Folder Two");
            Directory.CreateDirectory(appDir2);
            string exePath2 = Path.Combine(appDir2, "EasyHFP.exe");
            File.WriteAllText(exePath2, "dummy-binary-2");

            var shortcutService2 = new StartMenuShortcutService(shortcutPath, exePath2);
            Assert.True(shortcutService2.IsShortcutPresent());
            Assert.False(shortcutService2.IsShortcutValidForCurrentExecutable());

            // When preference is false, do not repair stale shortcut
            Assert.False(shortcutService2.RepairShortcutIfStale(addToStartMenuPreference: false));
            Assert.False(shortcutService2.IsShortcutValidForCurrentExecutable());

            // When preference is true, repair updates shortcut in place without creating duplicates
            Assert.True(shortcutService2.RepairShortcutIfStale(addToStartMenuPreference: true));
            Assert.True(shortcutService2.IsShortcutValidForCurrentExecutable());
            Assert.Equal(Path.GetFullPath(exePath2), Path.GetFullPath(shortcutService2.TryGetShortcutTargetPath()!), ignoreCase: true);
            Assert.Equal(Path.GetFullPath(appDir2), Path.GetFullPath(shortcutService2.TryGetShortcutWorkingDirectory()!), ignoreCase: true);
            Assert.Single(Directory.GetFiles(programsDir, "*.lnk"));

            // 3. Remove shortcut
            shortcutService2.SetShortcutEnabled(false);
            Assert.False(shortcutService2.IsShortcutPresent());
            Assert.False(shortcutService2.IsShortcutValidForCurrentExecutable());
        }

        [Fact]
        public void OnboardingWizard_WalksAllThreeSteps_SavesCompletedConfig_AndNeverActivatesMicrophone()
        {
            string fakeExePath = Path.Combine(_tempDir, "EasyHFP.exe");
            File.WriteAllText(fakeExePath, "dummy");

            string configPath = Path.Combine(_tempDir, "config.json");
            string shortcutPath = Path.Combine(_tempDir, "Programs", "EasyHFP.lnk");

            var configService = new ConfigurationService(configPath, null, _testRegistrySubKey, fakeExePath);
            var shortcutService = new StartMenuShortcutService(shortcutPath, fakeExePath);
            var fakeBt = new FakeBluetoothService();
            fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Test Studio Headset",
                Mac = "AABBCCDDEEFF",
                MacUlong = 0xAABBCCDDEEFF,
                Connected = true,
                HfpState = HfpServiceState.Enabled
            });

            var spyController = new SpyAudioProfileController();

            using var wizard = new OnboardingWizardForm(fakeBt, configService, shortcutService, spyController);

            // Step 1: Welcome & Headset Selection
            Assert.Equal(0, wizard.CurrentStepIndex);
            Assert.NotNull(wizard.SelectedDevice);
            Assert.Equal("AABBCCDDEEFF", wizard.SelectedDevice!.Mac);

            // Read-only snapshot was queried, but ZERO microphone capture switches occurred
            Assert.True(spyController.QuerySnapshotCount >= 1);
            Assert.Equal(0, spyController.SwitchToHfpCount);
            Assert.Equal(0, spyController.SwitchToA2dpCount);
            Assert.Equal(0, spyController.ToggleProfileCount);

            // Step 2: Preferences (verify required defaults)
            wizard.NavigateNext();
            Assert.Equal(1, wizard.CurrentStepIndex);
            Assert.True(wizard.AddToStartMenuChecked);
            Assert.False(wizard.StartWithWindowsChecked);
            Assert.True(wizard.CloseToTrayChecked);
            Assert.True(wizard.NotificationsChecked);

            // Step 3: Summary & Finish
            wizard.NavigateNext();
            Assert.Equal(2, wizard.CurrentStepIndex);

            wizard.CompleteOnboarding();

            // Verify zero microphone capture occurred throughout entire onboarding
            Assert.Equal(0, spyController.SwitchToHfpCount);
            Assert.Equal(0, spyController.ToggleProfileCount);

            // Verify persisted config.json
            var savedConfig = configService.LoadConfig();
            Assert.True(savedConfig.SetupCompleted);
            Assert.Equal("AABBCCDDEEFF", savedConfig.DefaultMac);
            Assert.Equal("Test Studio Headset", savedConfig.DefaultName);
            Assert.True(savedConfig.AddToStartMenu);
            Assert.False(savedConfig.StartWithWindows);
            Assert.True(savedConfig.MinimizeToTray);
            Assert.True(savedConfig.ShowNotifications);

            // Verify Start Menu shortcut was created and Windows Startup remains disabled
            Assert.True(shortcutService.IsShortcutValidForCurrentExecutable());
            Assert.False(configService.IsAutoStartEnabled());
        }

        [Fact]
        public void OnboardingWizard_WhenCancelled_DoesNotMarkSetupCompleted()
        {
            string configPath = Path.Combine(_tempDir, "config.json");
            string shortcutPath = Path.Combine(_tempDir, "Programs", "EasyHFP.lnk");

            var configService = new ConfigurationService(configPath);
            var shortcutService = new StartMenuShortcutService(shortcutPath);
            var fakeBt = new FakeBluetoothService();
            var spyController = new SpyAudioProfileController();

            using (var wizard = new OnboardingWizardForm(fakeBt, configService, shortcutService, spyController))
            {
                wizard.NavigateNext();
                // User closes/cancels without calling CompleteOnboarding()
            }

            var loaded = configService.LoadConfig();
            Assert.False(loaded.SetupCompleted);
            Assert.False(shortcutService.IsShortcutPresent());
        }

        [Fact]
        public void SingleInstance_WhileOnboardingIsOpen_SignalsOnboardingWindowToForeground()
        {
            string prefix = $@"Local\EasyHFP_OnboardingSingleInstance_{Guid.NewGuid():N}";
            string configPath = Path.Combine(_tempDir, "config.json");
            string shortcutPath = Path.Combine(_tempDir, "Programs", "EasyHFP.lnk");

            var configService = new ConfigurationService(configPath);
            var shortcutService = new StartMenuShortcutService(shortcutPath);
            var fakeBt = new FakeBluetoothService();
            var spyController = new SpyAudioProfileController();

            using var wakeupReceived = new ManualResetEventSlim(false);
            using var wizard = new OnboardingWizardForm(fakeBt, configService, shortcutService, spyController);

            using (var primary = new SingleInstanceCoordinator(prefix))
            {
                Assert.True(primary.IsPrimaryInstance);
                primary.RegisterWakeupCallback(() =>
                {
                    wizard.BringToFrontFromSecondaryInstance();
                    wakeupReceived.Set();
                });

                var secondaryThread = new Thread(() =>
                {
                    using var secondary = new SingleInstanceCoordinator(prefix);
                    Assert.False(secondary.IsPrimaryInstance);
                    secondary.SignalExistingInstanceToShow();
                });
                secondaryThread.Start();
                secondaryThread.Join();

                Assert.True(wakeupReceived.Wait(2000), "Onboarding wizard did not receive wakeup signal from secondary instance.");
                Assert.Equal(1, wizard.WakeupCount);
            }
        }

        [Fact]
        public void AppBranding_ProvidesMultiResolutionIcon_AndOfficialLogoInOnboarding()
        {
            byte[]? icoBytes = AppBranding.GetEmbeddedIconBytes();
            Assert.NotNull(icoBytes);
            Assert.True(icoBytes!.Length > 6 + 7 * 16);

            // ICONDIR header: Reserved=0, Type=1 (ICO), Count=7 (16, 24, 32, 48, 64, 128, 256)
            Assert.Equal(0, BitConverter.ToUInt16(icoBytes, 0));
            Assert.Equal(1, BitConverter.ToUInt16(icoBytes, 2));
            Assert.Equal(7, BitConverter.ToUInt16(icoBytes, 4));

            int[] expectedSizes = { 16, 24, 32, 48, 64, 128, 256 };
            for (int i = 0; i < expectedSizes.Length; i++)
            {
                int entryOffset = 6 + (i * 16);
                int width = icoBytes[entryOffset] == 0 ? 256 : icoBytes[entryOffset];
                int height = icoBytes[entryOffset + 1] == 0 ? 256 : icoBytes[entryOffset + 1];
                Assert.Equal(expectedSizes[i], width);
                Assert.Equal(expectedSizes[i], height);
            }

            using var icon16 = AppBranding.CreateIconForSize(16);
            Assert.Equal(16, icon16.Width);
            Assert.Equal(16, icon16.Height);

            using var logo = AppBranding.CreateLogoBitmap();
            Assert.NotNull(logo);
            Assert.Equal(logo!.Width, logo.Height);

            string configPath = Path.Combine(_tempDir, "config_logo.json");
            string shortcutPath = Path.Combine(_tempDir, "Programs", "EasyHFP.lnk");
            using var wizard = new OnboardingWizardForm(
                new FakeBluetoothService(),
                new ConfigurationService(configPath),
                new StartMenuShortcutService(shortcutPath),
                new SpyAudioProfileController());

            Assert.NotNull(wizard.HeaderLogoImage);
            Assert.NotNull(wizard.Icon);
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
