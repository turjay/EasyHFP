using EasyHFP.Cli;
using EasyHFP.Models;
using Xunit;

namespace EasyHFP.Tests
{
    public class CliParserTests
    {
        [Fact]
        public void Parse_NoArguments_ReturnsGuiAction()
        {
            var result = CliParser.Parse(Array.Empty<string>());
            Assert.True(result.IsValid);
            Assert.Equal(CliAction.Gui, result.Action);
        }

        [Theory]
        [InlineData("--unknown")]
        [InlineData("-x")]
        [InlineData("random_word")]
        public void Parse_UnknownArgument_ReturnsInvalid(string arg)
        {
            var result = CliParser.Parse(new[] { arg });
            Assert.False(result.IsValid);
            Assert.NotNull(result.ErrorMessage);
            Assert.Contains("Unknown or unexpected argument", result.ErrorMessage);
        }

        [Fact]
        public void Parse_MissingDeviceValue_ReturnsInvalid()
        {
            var result = CliParser.Parse(new[] { "--toggle", "--device" });
            Assert.False(result.IsValid);
            Assert.Contains("Missing required argument value for option '--device'", result.ErrorMessage);
        }

        [Fact]
        public void Parse_MissingReconnectModeValue_ReturnsInvalid()
        {
            var result = CliParser.Parse(new[] { "--toggle", "--reconnect-mode" });
            Assert.False(result.IsValid);
            Assert.Contains("Missing required argument value for option '--reconnect-mode'", result.ErrorMessage);
        }

        [Fact]
        public void Parse_InvalidReconnectMode_ReturnsInvalid()
        {
            var result = CliParser.Parse(new[] { "--toggle", "--reconnect-mode", "magic_mode" });
            Assert.False(result.IsValid);
            Assert.Contains("Invalid reconnect mode 'magic_mode'", result.ErrorMessage);
        }

        [Theory]
        [InlineData("auto", ReconnectMode.Auto)]
        [InlineData("pnp", ReconnectMode.Pnp)]
        [InlineData("none", ReconnectMode.None)]
        [InlineData("radio", ReconnectMode.Radio)]
        public void Parse_ValidReconnectMode_Succeeds(string modeStr, ReconnectMode expectedMode)
        {
            var result = CliParser.Parse(new[] { "--toggle", "--reconnect-mode", modeStr });
            Assert.True(result.IsValid);
            Assert.Equal(expectedMode, result.ReconnectModeOverride);
        }

        [Fact]
        public void Parse_ConflictingActions_ReturnsInvalid()
        {
            var result = CliParser.Parse(new[] { "--on", "--off" });
            Assert.False(result.IsValid);
            Assert.Contains("Conflicting actions specified", result.ErrorMessage);
        }

        [Fact]
        public void Parse_OptionsWithoutAction_DoesNotImplicitlyToggle()
        {
            // Providing only options without an action must NOT be treated as implicit toggle!
            var result = CliParser.Parse(new[] { "--device", "Sony WH-1000XM4" });
            Assert.False(result.IsValid);
            Assert.Contains("No action specified", result.ErrorMessage);
        }

        [Fact]
        public void Parse_ValidCombination_Succeeds()
        {
            var result = CliParser.Parse(new[] { "--toggle", "--device", "AABBCCDDEEFF", "--reconnect-mode", "auto", "--silent" });
            Assert.True(result.IsValid);
            Assert.Equal(CliAction.Toggle, result.Action);
            Assert.Equal("AABBCCDDEEFF", result.TargetDevice);
            Assert.Equal(ReconnectMode.Auto, result.ReconnectModeOverride);
            Assert.True(result.Silent);
        }
    }
}
