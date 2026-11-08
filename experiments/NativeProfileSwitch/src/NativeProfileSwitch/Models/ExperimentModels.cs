using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using NativeProfileSwitch.Interop;

namespace NativeProfileSwitch.Models
{
    public sealed class AudioFormatSummary
    {
        public ushort FormatTag { get; set; }
        public int Channels { get; set; }
        public int SampleRateHz { get; set; }
        public int BitsPerSample { get; set; }
        public int BlockAlign { get; set; }
        public string Summary => $"{Channels}ch {SampleRateHz}Hz {BitsPerSample}-bit (tag=0x{FormatTag:X4})";

        public static AudioFormatSummary FromWaveFormat(CoreAudioInterop.WAVEFORMATEX wf)
        {
            return new AudioFormatSummary
            {
                FormatTag = wf.wFormatTag,
                Channels = wf.nChannels,
                SampleRateHz = (int)wf.nSamplesPerSec,
                BitsPerSample = wf.wBitsPerSample,
                BlockAlign = wf.nBlockAlign
            };
        }
    }

    public sealed class AudioEndpointInfo
    {
        public string EndpointId { get; set; } = string.Empty;
        public string FriendlyName { get; set; } = string.Empty;
        public string DeviceDescription { get; set; } = string.Empty;
        public string InterfaceFriendlyName { get; set; } = string.Empty;
        public string DataFlow { get; set; } = string.Empty;
        public uint RawState { get; set; }
        public string StateText { get; set; } = string.Empty;
        public Guid? ContainerId { get; set; }
        public string? DeviceInstanceId { get; set; }
        public string? EnumeratorName { get; set; }
        public string? ExtractedBluetoothMac { get; set; }
        public string BluetoothTransportKind { get; set; } = "NonBluetooth";
        public bool IsBluetoothClassic { get; set; }
        public bool IsBluetoothLeAudio { get; set; }
        public uint? FormFactor { get; set; }
        public uint? PhysicalSpeakersMask { get; set; }
        public List<string> DefaultRoles { get; set; } = new();
        public AudioFormatSummary? PropertyStoreDeviceFormat { get; set; }
        public AudioFormatSummary? PropertyStoreOemFormat { get; set; }
        public AudioFormatSummary? DefaultMixFormat { get; set; }
        public AudioFormatSummary? CommunicationsMixFormat { get; set; }
        public int TotalSessionCount { get; set; }
        public int ActiveSessionCount { get; set; }
        public int SelfActiveSessionCount { get; set; }
        public int ExternalActiveSessionCount { get; set; }
        public List<uint> ExternalActiveProcessIds { get; set; } = new();
    }

    public sealed class HeadsetEndpointGroup
    {
        public string GroupKey { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public Guid? ContainerId { get; set; }
        public string? BluetoothMac { get; set; }
        public string AssociationMethod { get; set; } = string.Empty;
        public bool IsBluetoothClassic { get; set; }
        public bool IsBluetoothLeAudio { get; set; }
        public bool IsWindows11UnifiedEndpointTopology { get; set; }
        public bool HasAmbiguity { get; set; }
        public string? AmbiguityNotes { get; set; }
        public List<AudioEndpointInfo> RenderEndpoints { get; set; } = new();
        public List<AudioEndpointInfo> CaptureEndpoints { get; set; } = new();
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum ExperimentOutcome
    {
        NOT_RUN,
        API_SUCCESS_PROFILE_UNVERIFIED,
        PROFILE_TRANSITION_OBSERVED,
        TRANSITION_FAILED,
        INCONCLUSIVE
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum InferredTransportProfile
    {
        Unknown,
        A2dpStereo,
        HfpHandsFree,
        DisconnectedOrUnavailable,
        LeAudioUnapplicable
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum VerificationConfidence
    {
        None,
        Low_ApiReturnOnly,
        Medium_SessionAndFormatProxy,
        High_MultiSignalConfirmed
    }

    public sealed class EndpointNotificationRecord
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string EventType { get; set; } = string.Empty;
        public string EndpointId { get; set; } = string.Empty;
        public string Details { get; set; } = string.Empty;
    }

    public sealed class ApiCallLogEntry
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string ApiName { get; set; } = string.Empty;
        public int HResult { get; set; }
        public string HResultFormatted { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
    }

    public sealed class ProfileVerificationSnapshot
    {
        public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        public string PhaseLabel { get; set; } = string.Empty;
        public string? RenderEndpointId { get; set; }
        public string? CaptureEndpointId { get; set; }
        public string RenderStateText { get; set; } = "Unknown";
        public string CaptureStateText { get; set; } = "Unknown";
        public AudioFormatSummary? RenderDeviceFormat { get; set; }
        public AudioFormatSummary? RenderDefaultMixFormat { get; set; }
        public AudioFormatSummary? RenderCommunicationsMixFormat { get; set; }
        public AudioFormatSummary? CaptureDeviceFormat { get; set; }
        public int SelfActiveCaptureSessions { get; set; }
        public int ExternalActiveCaptureSessions { get; set; }
        public List<uint> ExternalCaptureProcessIds { get; set; } = new();
        public int SelfActiveRenderSessions { get; set; }
        public int ExternalActiveRenderSessions { get; set; }
        public int ObservedPropertyChangeEventsCount { get; set; }
        public int ObservedStateChangeEventsCount { get; set; }
        public InferredTransportProfile InferredProfile { get; set; } = InferredTransportProfile.Unknown;
        public VerificationConfidence Confidence { get; set; } = VerificationConfidence.None;
        public List<string> EvidenceSummary { get; set; } = new();
    }

    public sealed class ExperimentRunReport
    {
        public string RunId { get; set; } = Guid.NewGuid().ToString("N");
        public string ExperimentName { get; set; } = string.Empty;
        public DateTime StartTimeUtc { get; set; } = DateTime.UtcNow;
        public DateTime EndTimeUtc { get; set; }
        public string WindowsProductName { get; set; } = string.Empty;
        public string WindowsDisplayVersion { get; set; } = string.Empty;
        public int WindowsBuildNumber { get; set; }
        public bool IsWindows11 { get; set; }
        public string? SelectedRenderEndpointId { get; set; }
        public string? SelectedCaptureEndpointId { get; set; }
        public string? DeviceName { get; set; }
        public Guid? ContainerId { get; set; }
        public string? BluetoothMac { get; set; }
        public string StreamCategory { get; set; } = string.Empty;
        public string StreamDirection { get; set; } = string.Empty;
        public ExperimentOutcome Outcome { get; set; } = ExperimentOutcome.NOT_RUN;
        public double? TransitionToHfpDurationMs { get; set; }
        public double? ReturnToA2dpDurationMs { get; set; }
        public ProfileVerificationSnapshot? BaselineVerification { get; set; }
        public ProfileVerificationSnapshot? ActiveStreamVerification { get; set; }
        public ProfileVerificationSnapshot? PostReleaseVerification { get; set; }
        public List<ApiCallLogEntry> ApiCalls { get; set; } = new();
        public List<EndpointNotificationRecord> NotificationEvents { get; set; } = new();
        public List<string> Notes { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }
}
