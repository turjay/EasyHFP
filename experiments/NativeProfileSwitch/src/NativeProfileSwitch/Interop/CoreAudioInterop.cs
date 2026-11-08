using System;
using System.Runtime.InteropServices;

namespace NativeProfileSwitch.Interop
{
    public static class CoreAudioInterop
    {
        public const uint DEVICE_STATE_ACTIVE = 0x00000001;
        public const uint DEVICE_STATE_DISABLED = 0x00000002;
        public const uint DEVICE_STATE_NOTPRESENT = 0x00000004;
        public const uint DEVICE_STATE_UNPLUGGED = 0x00000008;
        public const uint DEVICE_STATEMASK_ALL = 0x0000000F;

        public const uint STGM_READ = 0x00000000;
        public const uint CLSCTX_ALL = 0x17;

        // AUDCLNT stream flags
        public const uint AUDCLNT_STREAMFLAGS_CROSSPROCESS = 0x00010000;
        public const uint AUDCLNT_STREAMFLAGS_LOOPBACK = 0x00020000;
        public const uint AUDCLNT_STREAMFLAGS_EVENTCALLBACK = 0x00040000;
        public const uint AUDCLNT_STREAMFLAGS_NOPERSIST = 0x00080000;
        public const uint AUDCLNT_STREAMFLAGS_RATEADJUST = 0x00100000;
        public const uint AUDCLNT_STREAMFLAGS_AUTOCONVERTPCM = 0x80000000;
        public const uint AUDCLNT_STREAMFLAGS_SRC_DEFAULT_QUALITY = 0x08000000;

        // AUDCLNT buffer flags
        public const uint AUDCLNT_BUFFERFLAGS_DATA_DISCONTINUITY = 0x1;
        public const uint AUDCLNT_BUFFERFLAGS_SILENT = 0x2;
        public const uint AUDCLNT_BUFFERFLAGS_TIMESTAMP_ERROR = 0x4;

        // Common Core Audio HRESULTs
        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int E_NOTFOUND = unchecked((int)0x80070490);
        public const int AUDCLNT_E_NOT_INITIALIZED = unchecked((int)0x88890001);
        public const int AUDCLNT_E_ALREADY_INITIALIZED = unchecked((int)0x88890002);
        public const int AUDCLNT_E_WRONG_ENDPOINT_TYPE = unchecked((int)0x88890003);
        public const int AUDCLNT_E_DEVICE_INVALIDATED = unchecked((int)0x88890004);
        public const int AUDCLNT_E_NOT_STOPPED = unchecked((int)0x88890005);
        public const int AUDCLNT_E_BUFFER_TOO_LARGE = unchecked((int)0x88890006);
        public const int AUDCLNT_E_OUT_OF_ORDER = unchecked((int)0x88890007);
        public const int AUDCLNT_E_UNSUPPORTED_FORMAT = unchecked((int)0x88890008);
        public const int AUDCLNT_E_INVALID_SIZE = unchecked((int)0x88890009);
        public const int AUDCLNT_E_DEVICE_IN_USE = unchecked((int)0x8889000A);
        public const int AUDCLNT_E_BUFFER_OPERATION_PENDING = unchecked((int)0x8889000B);
        public const int AUDCLNT_E_THREAD_NOT_REGISTERED = unchecked((int)0x8889000C);
        public const int AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED = unchecked((int)0x8889000E);
        public const int AUDCLNT_E_ENDPOINT_CREATE_FAILED = unchecked((int)0x8889000F);
        public const int AUDCLNT_E_SERVICE_NOT_RUNNING = unchecked((int)0x88890010);
        public const int AUDCLNT_E_EVENTHANDLE_NOT_EXPECTED = unchecked((int)0x88890011);
        public const int AUDCLNT_E_CPUUSAGE_EXCEEDED = unchecked((int)0x88890017);
        public const int AUDCLNT_E_BUFFER_ERROR = unchecked((int)0x88890018);
        public const int AUDCLNT_E_BUFFER_SIZE_NOT_ALIGNED = unchecked((int)0x88890019);
        public const int AUDCLNT_E_RESOURCES_INVALIDATED = unchecked((int)0x88890026);
        public const int AUDCLNT_S_BUFFER_EMPTY = 0x08890001;

        // COM IIDs
        public static readonly Guid IID_IAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
        public static readonly Guid IID_IAudioClient2 = new("726778CD-F60A-4eda-82DE-E47610CD78AA");
        public static readonly Guid IID_IAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
        public static readonly Guid IID_IAudioRenderClient = new("F294ACFC-3146-4483-A7BF-ADDCA7C260E2");
        public static readonly Guid IID_IAudioSessionManager2 = new("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F");

        // Property Keys
        public static readonly PROPERTYKEY PKEY_Device_FriendlyName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        public static readonly PROPERTYKEY PKEY_Device_DeviceDesc = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 2);
        public static readonly PROPERTYKEY PKEY_DeviceInterface_FriendlyName = new(new Guid("026e516e-b814-414b-83cd-856d6fef4822"), 2);
        public static readonly PROPERTYKEY PKEY_Device_ContainerId = new(new Guid("8c7ed206-3f8a-4827-b3ab-ae9e1faefc6c"), 2);
        public static readonly PROPERTYKEY PKEY_Device_InstanceId = new(new Guid("78c34fc8-104a-4aca-9ea4-524d52996e57"), 256);
        public static readonly PROPERTYKEY PKEY_Device_EnumeratorName = new(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 24);
        public static readonly PROPERTYKEY PKEY_AudioEndpoint_Interface = new(new Guid("b3f8fa53-0004-438e-9003-51a46e139bfc"), 2);
        public static readonly PROPERTYKEY PKEY_AudioEndpoint_FormFactor = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 0);
        public static readonly PROPERTYKEY PKEY_AudioEndpoint_Association = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 2);
        public static readonly PROPERTYKEY PKEY_AudioEndpoint_PhysicalSpeakers = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 3);
        public static readonly PROPERTYKEY PKEY_AudioEndpoint_GUID = new(new Guid("1da5d803-d492-4edd-8c23-e0c0ffee7f0e"), 4);
        public static readonly PROPERTYKEY PKEY_AudioEngine_DeviceFormat = new(new Guid("f19f064d-082c-4e27-bc73-6882a1bb8e4c"), 0);
        public static readonly PROPERTYKEY PKEY_AudioEngine_OEMFormat = new(new Guid("e4870e26-3cc5-4cd2-ba46-ca0a9a70ed04"), 3);

        // VARTYPE constants
        public const ushort VT_EMPTY = 0;
        public const ushort VT_UI4 = 19;
        public const ushort VT_LPWSTR = 31;
        public const ushort VT_BLOB = 65;
        public const ushort VT_CLSID = 72;

        public enum EDataFlow
        {
            eRender = 0,
            eCapture = 1,
            eAll = 2
        }

        public enum ERole
        {
            eConsole = 0,
            eMultimedia = 1,
            eCommunications = 2
        }

        public enum AUDCLNT_SHAREMODE
        {
            AUDCLNT_SHAREMODE_SHARED = 0,
            AUDCLNT_SHAREMODE_EXCLUSIVE = 1
        }

        public enum AUDIO_STREAM_CATEGORY
        {
            AudioCategory_Other = 0,
            AudioCategory_ForegroundOnlyMedia = 1,
            AudioCategory_Communications = 3,
            AudioCategory_Alerts = 4,
            AudioCategory_SoundEffects = 5,
            AudioCategory_GameEffects = 6,
            AudioCategory_GameMedia = 7,
            AudioCategory_GameChat = 8,
            AudioCategory_Speech = 9,
            AudioCategory_Movie = 10,
            AudioCategory_Media = 11
        }

        [Flags]
        public enum AUDCLNT_STREAMOPTIONS
        {
            AUDCLNT_STREAMOPTIONS_NONE = 0x0,
            AUDCLNT_STREAMOPTIONS_RAW = 0x1,
            AUDCLNT_STREAMOPTIONS_MATCH_FORMAT = 0x2,
            AUDCLNT_STREAMOPTIONS_AMBISONICS = 0x4
        }

        public enum AudioSessionState
        {
            AudioSessionStateInactive = 0,
            AudioSessionStateActive = 1,
            AudioSessionStateExpired = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PROPERTYKEY
        {
            public Guid fmtid;
            public uint pid;

            public PROPERTYKEY(Guid fmtid, uint pid)
            {
                this.fmtid = fmtid;
                this.pid = pid;
            }

            public override string ToString() => $"{fmtid:B},{pid}";
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLOB
        {
            public uint cbSize;
            public IntPtr pBlobData;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct PROPVARIANT
        {
            [FieldOffset(0)]
            public ushort vt;
            [FieldOffset(2)]
            public ushort wReserved1;
            [FieldOffset(4)]
            public ushort wReserved2;
            [FieldOffset(6)]
            public ushort wReserved3;
            [FieldOffset(8)]
            public IntPtr pointerVal;
            [FieldOffset(8)]
            public uint uintVal;
            [FieldOffset(8)]
            public int intVal;
            [FieldOffset(8)]
            public BLOB blobVal;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct AudioClientProperties
        {
            public uint cbSize;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bIsOffload;
            public AUDIO_STREAM_CATEGORY eCategory;
            public AUDCLNT_STREAMOPTIONS Options;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WAVEFORMATEX
        {
            public ushort wFormatTag;
            public ushort nChannels;
            public uint nSamplesPerSec;
            public uint nAvgBytesPerSec;
            public ushort nBlockAlign;
            public ushort wBitsPerSample;
            public ushort cbSize;
        }

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PROPVARIANT pvar);

        [DllImport("ole32.dll")]
        public static extern void CoTaskMemFree(IntPtr pv);

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        public class MMDeviceEnumeratorComObject
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(EDataFlow dataFlow, uint dwStateMask, out IMMDeviceCollection ppDevices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role, out IMMDevice ppEndpoint);

            [PreserveSig]
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string pwstrId, out IMMDevice ppDevice);

            [PreserveSig]
            int RegisterEndpointNotificationCallback(IMMNotificationClient pClient);

            [PreserveSig]
            int UnregisterEndpointNotificationCallback(IMMNotificationClient pClient);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDeviceCollection
        {
            [PreserveSig]
            int GetCount(out uint pcDevices);

            [PreserveSig]
            int Item(uint nDevice, out IMMDevice ppDevice);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid iid, uint dwClsCtx, IntPtr pActivationParams, [MarshalAs(UnmanagedType.IUnknown)] out object ppInterface);

            [PreserveSig]
            int OpenPropertyStore(uint stgmAccess, out IPropertyStore ppProperties);

            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string ppstrId);

            [PreserveSig]
            int GetState(out uint pdwState);
        }

        [ComImport]
        [Guid("1BE09788-6894-4089-8586-9A2A6C265AC5")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMEndpoint
        {
            [PreserveSig]
            int GetDataFlow(out EDataFlow pDataFlow);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IPropertyStore
        {
            [PreserveSig]
            int GetCount(out uint cProps);

            [PreserveSig]
            int GetAt(uint iProp, out PROPERTYKEY pkey);

            [PreserveSig]
            int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);

            [PreserveSig]
            int SetValue(ref PROPERTYKEY key, ref PROPVARIANT propvar);

            [PreserveSig]
            int Commit();
        }

        [ComImport]
        [Guid("7991EEC9-7E89-4D85-8390-6C7020460A77")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IMMNotificationClient
        {
            void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, uint dwNewState);
            void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
            void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId);
            void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string pwstrDefaultDeviceId);
            void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string pwstrDeviceId, PROPERTYKEY key);
        }

        [ComImport]
        [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient
        {
            [PreserveSig]
            int Initialize(
                AUDCLNT_SHAREMODE ShareMode,
                uint StreamFlags,
                long hnsBufferDuration,
                long hnsPeriodicity,
                IntPtr pFormat,
                IntPtr AudioSessionGuid);

            [PreserveSig]
            int GetBufferSize(out uint pNumBufferFrames);

            [PreserveSig]
            int GetStreamLatency(out long phnsLatency);

            [PreserveSig]
            int GetCurrentPadding(out uint pNumPaddingFrames);

            [PreserveSig]
            int IsFormatSupported(
                AUDCLNT_SHAREMODE ShareMode,
                IntPtr pFormat,
                out IntPtr ppClosestMatch);

            [PreserveSig]
            int GetMixFormat(out IntPtr ppDeviceFormat);

            [PreserveSig]
            int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);

            [PreserveSig]
            int Start();

            [PreserveSig]
            int Stop();

            [PreserveSig]
            int Reset();

            [PreserveSig]
            int SetEventHandle(IntPtr eventHandle);

            [PreserveSig]
            int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);
        }

        [ComImport]
        [Guid("726778CD-F60A-4eda-82DE-E47610CD78AA")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioClient2
        {
            // IAudioClient base methods (VTable slots 3..14)
            [PreserveSig]
            int Initialize(
                AUDCLNT_SHAREMODE ShareMode,
                uint StreamFlags,
                long hnsBufferDuration,
                long hnsPeriodicity,
                IntPtr pFormat,
                IntPtr AudioSessionGuid);

            [PreserveSig]
            int GetBufferSize(out uint pNumBufferFrames);

            [PreserveSig]
            int GetStreamLatency(out long phnsLatency);

            [PreserveSig]
            int GetCurrentPadding(out uint pNumPaddingFrames);

            [PreserveSig]
            int IsFormatSupported(
                AUDCLNT_SHAREMODE ShareMode,
                IntPtr pFormat,
                out IntPtr ppClosestMatch);

            [PreserveSig]
            int GetMixFormat(out IntPtr ppDeviceFormat);

            [PreserveSig]
            int GetDevicePeriod(out long phnsDefaultDevicePeriod, out long phnsMinimumDevicePeriod);

            [PreserveSig]
            int Start();

            [PreserveSig]
            int Stop();

            [PreserveSig]
            int Reset();

            [PreserveSig]
            int SetEventHandle(IntPtr eventHandle);

            [PreserveSig]
            int GetService(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object ppv);

            // IAudioClient2 methods (VTable slots 15..17)
            [PreserveSig]
            int IsOffloadCapable(
                AUDIO_STREAM_CATEGORY Category,
                [MarshalAs(UnmanagedType.Bool)] out bool pbOffloadCapable);

            [PreserveSig]
            int SetClientProperties([In] ref AudioClientProperties pProperties);

            [PreserveSig]
            int GetBufferSizeLimits(
                IntPtr pFormat,
                [MarshalAs(UnmanagedType.Bool)] bool bEventDriven,
                out long phnsMinBufferDuration,
                out long phnsMaxBufferDuration);
        }

        [ComImport]
        [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioCaptureClient
        {
            [PreserveSig]
            int GetBuffer(
                out IntPtr ppData,
                out uint pNumFramesToRead,
                out uint pdwFlags,
                out ulong pu64DevicePosition,
                out ulong pu64QPCPosition);

            [PreserveSig]
            int ReleaseBuffer(uint NumFramesRead);

            [PreserveSig]
            int GetNextPacketSize(out uint pNumFramesInNextPacket);
        }

        [ComImport]
        [Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioRenderClient
        {
            [PreserveSig]
            int GetBuffer(uint NumFramesRequested, out IntPtr ppData);

            [PreserveSig]
            int ReleaseBuffer(uint NumFramesWritten, uint dwFlags);
        }

        [ComImport]
        [Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionManager2
        {
            [PreserveSig]
            int GetAudioSessionControl(IntPtr AudioSessionGuid, uint StreamFlags, out IAudioSessionControl SessionControl);

            [PreserveSig]
            int GetSimpleAudioVolume(IntPtr AudioSessionGuid, uint StreamFlags, [MarshalAs(UnmanagedType.IUnknown)] out object AudioVolume);

            [PreserveSig]
            int GetSessionEnumerator(out IAudioSessionEnumerator SessionEnum);

            [PreserveSig]
            int RegisterSessionNotification([MarshalAs(UnmanagedType.IUnknown)] object SessionNotification);

            [PreserveSig]
            int UnregisterSessionNotification([MarshalAs(UnmanagedType.IUnknown)] object SessionNotification);

            [PreserveSig]
            int RegisterDuckNotification([MarshalAs(UnmanagedType.LPWStr)] string sessionID, [MarshalAs(UnmanagedType.IUnknown)] object duckNotification);

            [PreserveSig]
            int UnregisterDuckNotification([MarshalAs(UnmanagedType.IUnknown)] object duckNotification);
        }

        [ComImport]
        [Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionEnumerator
        {
            [PreserveSig]
            int GetCount(out int SessionCount);

            [PreserveSig]
            int GetSession(int SessionCount, out IAudioSessionControl Session);
        }

        [ComImport]
        [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionControl
        {
            [PreserveSig]
            int GetState(out AudioSessionState pRetVal);

            [PreserveSig]
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

            [PreserveSig]
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

            [PreserveSig]
            int GetGroupingParam(out Guid pRetVal);

            [PreserveSig]
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);

            [PreserveSig]
            int RegisterAudioSessionNotification([MarshalAs(UnmanagedType.IUnknown)] object NewNotifications);

            [PreserveSig]
            int UnregisterAudioSessionNotification([MarshalAs(UnmanagedType.IUnknown)] object NewNotifications);
        }

        [ComImport]
        [Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IAudioSessionControl2
        {
            // IAudioSessionControl base methods
            [PreserveSig]
            int GetState(out AudioSessionState pRetVal);

            [PreserveSig]
            int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

            [PreserveSig]
            int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string Value, ref Guid EventContext);

            [PreserveSig]
            int GetGroupingParam(out Guid pRetVal);

            [PreserveSig]
            int SetGroupingParam(ref Guid Override, ref Guid EventContext);

            [PreserveSig]
            int RegisterAudioSessionNotification([MarshalAs(UnmanagedType.IUnknown)] object NewNotifications);

            [PreserveSig]
            int UnregisterAudioSessionNotification([MarshalAs(UnmanagedType.IUnknown)] object NewNotifications);

            // IAudioSessionControl2 methods
            [PreserveSig]
            int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string pRetVal);

            [PreserveSig]
            int GetProcessId(out uint pRetVal);

            [PreserveSig]
            int IsSystemSoundsSession();

            [PreserveSig]
            int SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
        }

        public static string FormatHResult(int hr)
        {
            string symbolic = hr switch
            {
                S_OK => "S_OK",
                S_FALSE => "S_FALSE",
                E_NOTFOUND => "E_NOTFOUND",
                AUDCLNT_E_NOT_INITIALIZED => "AUDCLNT_E_NOT_INITIALIZED",
                AUDCLNT_E_ALREADY_INITIALIZED => "AUDCLNT_E_ALREADY_INITIALIZED",
                AUDCLNT_E_WRONG_ENDPOINT_TYPE => "AUDCLNT_E_WRONG_ENDPOINT_TYPE",
                AUDCLNT_E_DEVICE_INVALIDATED => "AUDCLNT_E_DEVICE_INVALIDATED",
                AUDCLNT_E_NOT_STOPPED => "AUDCLNT_E_NOT_STOPPED",
                AUDCLNT_E_BUFFER_TOO_LARGE => "AUDCLNT_E_BUFFER_TOO_LARGE",
                AUDCLNT_E_OUT_OF_ORDER => "AUDCLNT_E_OUT_OF_ORDER",
                AUDCLNT_E_UNSUPPORTED_FORMAT => "AUDCLNT_E_UNSUPPORTED_FORMAT",
                AUDCLNT_E_INVALID_SIZE => "AUDCLNT_E_INVALID_SIZE",
                AUDCLNT_E_DEVICE_IN_USE => "AUDCLNT_E_DEVICE_IN_USE",
                AUDCLNT_E_BUFFER_OPERATION_PENDING => "AUDCLNT_E_BUFFER_OPERATION_PENDING",
                AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED => "AUDCLNT_E_EXCLUSIVE_MODE_NOT_ALLOWED",
                AUDCLNT_E_ENDPOINT_CREATE_FAILED => "AUDCLNT_E_ENDPOINT_CREATE_FAILED",
                AUDCLNT_E_SERVICE_NOT_RUNNING => "AUDCLNT_E_SERVICE_NOT_RUNNING",
                AUDCLNT_E_RESOURCES_INVALIDATED => "AUDCLNT_E_RESOURCES_INVALIDATED",
                AUDCLNT_S_BUFFER_EMPTY => "AUDCLNT_S_BUFFER_EMPTY",
                _ => "HRESULT"
            };
            return $"0x{hr:X8} ({symbolic})";
        }

        public static string FormatEndpointState(uint state)
        {
            return state switch
            {
                DEVICE_STATE_ACTIVE => "Active",
                DEVICE_STATE_DISABLED => "Disabled",
                DEVICE_STATE_NOTPRESENT => "NotPresent",
                DEVICE_STATE_UNPLUGGED => "Unplugged",
                _ => $"0x{state:X8}"
            };
        }

        public static string? ReadPropertyString(IPropertyStore store, PROPERTYKEY key)
        {
            var pk = key;
            if (store.GetValue(ref pk, out var pv) != S_OK)
            {
                return null;
            }

            try
            {
                if (pv.vt == VT_LPWSTR && pv.pointerVal != IntPtr.Zero)
                {
                    return Marshal.PtrToStringUni(pv.pointerVal);
                }
                return null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }

        public static uint? ReadPropertyUint(IPropertyStore store, PROPERTYKEY key)
        {
            var pk = key;
            if (store.GetValue(ref pk, out var pv) != S_OK)
            {
                return null;
            }

            try
            {
                if (pv.vt == VT_UI4)
                {
                    return pv.uintVal;
                }
                return null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }

        public static Guid? ReadPropertyGuid(IPropertyStore store, PROPERTYKEY key)
        {
            var pk = key;
            if (store.GetValue(ref pk, out var pv) != S_OK)
            {
                return null;
            }

            try
            {
                if (pv.vt == VT_CLSID && pv.pointerVal != IntPtr.Zero)
                {
                    return Marshal.PtrToStructure<Guid>(pv.pointerVal);
                }
                if (pv.vt == VT_LPWSTR && pv.pointerVal != IntPtr.Zero)
                {
                    string? str = Marshal.PtrToStringUni(pv.pointerVal);
                    if (Guid.TryParse(str, out var g))
                    {
                        return g;
                    }
                }
                return null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }

        public static WAVEFORMATEX? ReadPropertyWaveFormat(IPropertyStore store, PROPERTYKEY key)
        {
            var pk = key;
            if (store.GetValue(ref pk, out var pv) != S_OK)
            {
                return null;
            }

            try
            {
                if (pv.vt == VT_BLOB && pv.blobVal.pBlobData != IntPtr.Zero &&
                    pv.blobVal.cbSize >= Marshal.SizeOf<WAVEFORMATEX>())
                {
                    return Marshal.PtrToStructure<WAVEFORMATEX>(pv.blobVal.pBlobData);
                }
                return null;
            }
            finally
            {
                PropVariantClear(ref pv);
            }
        }
    }
}
