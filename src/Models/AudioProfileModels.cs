using System;
using System.Collections.Generic;

namespace EasyHFP.Models
{
    public enum AudioProfileMode
    {
        Unknown = 0,
        A2dp = 1,
        HfpHeldByEasyHfp = 2,
        HfpHeldByExternalApp = 3,
        Disconnected = 4
    }

    public enum ProfileTransitionOutcome
    {
        SwitchedToHfp = 0,
        SwitchedToA2dp = 1,
        ReleasedEasyHfpHoldButExternalAppActive = 2,
        CannotReturnToA2dpExternalAppActive = 3,
        AlreadyInTargetProfile = 4,
        CaptureEndpointUnavailable = 5,
        DeviceDisconnected = 6,
        MicrophoneAccessDenied = 7,
        TransitionFailed = 8
    }

    public sealed class HeadsetAudioEndpointMap
    {
        public string BluetoothMac { get; set; } = string.Empty;
        public string DeviceName { get; set; } = string.Empty;
        public Guid? ContainerId { get; set; }
        public string? RenderEndpointId { get; set; }
        public string? CaptureEndpointId { get; set; }
        public bool IsRenderActive { get; set; }
        public bool IsCaptureActive { get; set; }
        public int SelfActiveCaptureSessionCount { get; set; }
        public int ExternalActiveCaptureSessionCount { get; set; }
        public List<uint> ExternalCaptureProcessIds { get; set; } = new();
    }

    public sealed class HeadsetProfileSnapshot
    {
        public string BluetoothMac { get; init; } = string.Empty;
        public string DeviceName { get; init; } = string.Empty;
        public bool IsBluetoothConnected { get; init; }
        public HfpServiceState HfpServiceRegistrationState { get; init; } = HfpServiceState.Unknown;
        public Guid? ContainerId { get; init; }
        public string? RenderEndpointId { get; init; }
        public string? CaptureEndpointId { get; init; }
        public bool IsRenderEndpointActive { get; init; }
        public bool IsCaptureEndpointActive { get; init; }
        public bool IsHeldByEasyHfp { get; init; }
        public int SelfActiveCaptureSessions { get; init; }
        public int ExternalActiveCaptureSessions { get; init; }
        public IReadOnlyList<uint> ExternalCaptureProcessIds { get; init; } = Array.Empty<uint>();
        public AudioProfileMode InferredMode { get; init; } = AudioProfileMode.Unknown;
        public string ModeDisplayText { get; init; } = "Unknown";
        public string StatusDetail { get; init; } = string.Empty;

        public bool IsHfpActive =>
            InferredMode == AudioProfileMode.HfpHeldByEasyHfp ||
            InferredMode == AudioProfileMode.HfpHeldByExternalApp;
    }

    public sealed class ProfileTransitionResult
    {
        public bool Success { get; init; }
        public ProfileTransitionOutcome Outcome { get; init; }
        public AudioProfileMode TargetMode { get; init; }
        public AudioProfileMode ObservedMode { get; init; }
        public TimeSpan Duration { get; init; }
        public long FramesDiscarded { get; init; }
        public IReadOnlyList<uint> ExternalCaptureProcessIds { get; init; } = Array.Empty<uint>();
        public string UserMessage { get; init; } = string.Empty;
        public string? DiagnosticDetails { get; init; }
    }
}
