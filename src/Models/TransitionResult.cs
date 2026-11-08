using System;

namespace EasyHFP.Models
{
    public class TransitionResult
    {
        public bool Success { get; set; }
        public string TargetMac { get; set; } = string.Empty;
        public string TargetName { get; set; } = string.Empty;
        public HfpServiceState RequestedState { get; set; }
        public HfpServiceState FinalState { get; set; } = HfpServiceState.Unknown;

        // Service change step
        public bool ServiceChangeAttempted { get; set; }
        public bool ServiceChangeSuccess { get; set; }
        public uint ServiceChangeErrorCode { get; set; }

        // Reconnect / Recovery step
        public bool ReconnectAttempted { get; set; }
        public bool ReconnectSuccess { get; set; }
        public bool TargetRecoveryAttempted { get; set; }
        public bool TargetRecoverySuccess { get; set; }
        public string ReconnectStrategyUsed { get; set; } = "none";
        public bool RadioResetAttempted { get; set; }

        // Verification step
        public bool VerificationAttempted { get; set; }
        public bool VerificationPassed { get; set; }

        // Timings
        public TimeSpan ServiceCallDuration { get; set; }
        public TimeSpan VerificationDuration { get; set; }
        public TimeSpan RecoveryDuration { get; set; }
        public TimeSpan TotalDuration { get; set; }

        // Diagnostics / Errors
        public string? ErrorMessage { get; set; }
        public string? DiagnosticDetails { get; set; }
    }
}
