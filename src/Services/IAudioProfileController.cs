using System;
using System.Threading;
using System.Threading.Tasks;
using EasyHFP.Models;

namespace EasyHFP.Services
{
    public sealed record WasapiHoldStartResult(
        bool Started,
        int HResult,
        long InitialFramesDiscarded,
        double FirstPacketLatencyMs,
        string? ErrorMessage = null);

    public interface ICoreAudioEndpointMapper
    {
        HeadsetAudioEndpointMap ResolveHeadsetEndpoints(string bluetoothMac, string deviceName);
    }

    public interface IWasapiCaptureHoldSession : IDisposable
    {
        string TargetMac { get; }
        string CaptureEndpointId { get; }
        bool IsRunning { get; }
        long TotalFramesDiscarded { get; }
        event Action<string>? SessionInvalidated;

        Task<WasapiHoldStartResult> StartAsync(CancellationToken cancellationToken = default);
        Task StopAndReleaseAsync();
    }

    public interface IAudioProfileController : IDisposable
    {
        bool IsHoldingHfpStream { get; }
        string? ActiveHoldDeviceMac { get; }
        event Action? ProfileStateChanged;

        HeadsetProfileSnapshot QueryProfileSnapshot(BtDeviceItem device);
        Task<ProfileTransitionResult> SwitchToHfpAsync(BtDeviceItem device, CancellationToken cancellationToken = default);
        Task<ProfileTransitionResult> SwitchToA2dpAsync(BtDeviceItem device, CancellationToken cancellationToken = default);
        Task<ProfileTransitionResult> ToggleProfileAsync(BtDeviceItem device, CancellationToken cancellationToken = default);
        void ReleaseAllHolds();
    }
}
