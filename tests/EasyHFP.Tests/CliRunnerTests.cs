using System;
using System.Collections.Generic;
using EasyHFP.Cli;
using EasyHFP.Models;
using EasyHFP.Services;
using EasyHFP.Tests.Fakes;
using Xunit;

namespace EasyHFP.Tests
{
    public class CliRunnerTests
    {
        private readonly FakeBluetoothService _fakeBt;
        private readonly ConfigurationService _configService;

        public CliRunnerTests()
        {
            _fakeBt = new FakeBluetoothService();
            // Use temporary config path for testing
            string tempConfig = Path.Combine(Path.GetTempPath(), $"easyhfp_test_{Guid.NewGuid():N}.txt");
            _configService = new ConfigurationService(tempConfig);
        }

        [Fact]
        public void InvalidInput_ProducesZeroMutations_AndReturnsCode1()
        {
            var runner = new CliRunner(_fakeBt, _configService);
            var parsed = CliParser.Parse(new[] { "--invalid-flag" });

            int exitCode = runner.Run(parsed);

            Assert.Equal(1, exitCode);
            Assert.Equal(0, _fakeBt.MutationCount);
        }

        [Fact]
        public void AmbiguousDeviceName_DoesNotPickFirstDevice_AndReturnsCode2()
        {
            _fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Sony WH-1000XM4 (Living Room)",
                Mac = "112233445566",
                MacUlong = 0x112233445566,
                Connected = true,
                HfpState = HfpServiceState.Enabled
            });
            _fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Sony WH-1000XM4 (Office)",
                Mac = "AABBCCDDEEFF",
                MacUlong = 0xAABBCCDDEEFF,
                Connected = true,
                HfpState = HfpServiceState.Enabled
            });

            var runner = new CliRunner(_fakeBt, _configService);
            var parsed = CliParser.Parse(new[] { "--toggle", "--device", "Sony" });

            int exitCode = runner.Run(parsed);

            // Must reject ambiguous device and perform 0 mutations!
            Assert.Equal(2, exitCode);
            Assert.Equal(0, _fakeBt.MutationCount);
        }

        [Fact]
        public void AutomationCommand_WithoutConfiguredDevice_DoesNotPromptInteractive_AndReturnsCode2()
        {
            // Empty config (no default MAC)
            var config = _configService.LoadConfig();
            config.DefaultMac = "";
            _configService.SaveConfig(config);

            var runner = new CliRunner(_fakeBt, _configService);
            var parsed = CliParser.Parse(new[] { "--toggle" });

            int exitCode = runner.Run(parsed);

            Assert.Equal(2, exitCode);
            Assert.Equal(0, _fakeBt.MutationCount);
        }

        [Fact]
        public void DeviceNotFound_ReturnsCode2_AndZeroMutations()
        {
            _fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Headset A",
                Mac = "111111111111",
                MacUlong = 0x111111111111,
                Connected = true,
                HfpState = HfpServiceState.Disabled
            });

            var runner = new CliRunner(_fakeBt, _configService);
            var parsed = CliParser.Parse(new[] { "--toggle", "--device", "NonExistentDevice" });

            int exitCode = runner.Run(parsed);

            Assert.Equal(2, exitCode);
            Assert.Equal(0, _fakeBt.MutationCount);
        }

        [Fact]
        public void SuccessfulToggle_MutatesTarget_AndReturnsCode0()
        {
            _fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Headset A",
                Mac = "111111111111",
                MacUlong = 0x111111111111,
                Connected = true,
                HfpState = HfpServiceState.Disabled
            });

            var runner = new CliRunner(_fakeBt, _configService);
            var parsed = CliParser.Parse(new[] { "--toggle", "--device", "111111111111" });

            int exitCode = runner.Run(parsed);

            Assert.Equal(0, exitCode);
            Assert.Equal(1, _fakeBt.MutationCount);
            Assert.NotNull(_fakeBt.LastExecutedSnapshot);
            Assert.Equal(HfpServiceState.Enabled, _fakeBt.LastExecutedSnapshot.TargetHfpState);
        }

        [Fact]
        public void FailedTransition_ReturnsCode3()
        {
            _fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Headset A",
                Mac = "111111111111",
                MacUlong = 0x111111111111,
                Connected = true,
                HfpState = HfpServiceState.Disabled
            });
            _fakeBt.ServiceCallSucceeds = false; // Trigger failure

            var runner = new CliRunner(_fakeBt, _configService);
            var parsed = CliParser.Parse(new[] { "--toggle", "--device", "111111111111" });

            int exitCode = runner.Run(parsed);

            Assert.Equal(3, exitCode);
        }

        [Fact]
        public void SelectMode_OnEofInput_ReturnsCode1_WithoutHangingOrCrashing()
        {
            _fakeBt.Devices.Add(new BtDeviceItem
            {
                Name = "Headset A",
                Mac = "111111111111",
                MacUlong = 0x111111111111,
                Connected = true,
                HfpState = HfpServiceState.Disabled
            });

            var originalIn = Console.In;
            try
            {
                // Simulate EOF by providing empty reader
                Console.SetIn(new System.IO.StringReader(string.Empty));

                var runner = new CliRunner(_fakeBt, _configService);
                var parsed = CliParser.Parse(new[] { "--select" });

                int exitCode = runner.Run(parsed);

                Assert.Equal(1, exitCode);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }
    }
}
