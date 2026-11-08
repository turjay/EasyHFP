namespace EasyHFP.Interop
{
    public interface INativePnpAdapter
    {
        NativePnp.PnpRestartResult RestartDeviceNodes(string macHex, int waitMs = 800);
    }

    public class Win32PnpAdapter : INativePnpAdapter
    {
        public NativePnp.PnpRestartResult RestartDeviceNodes(string macHex, int waitMs = 800)
            => NativePnp.RestartDeviceNodes(macHex, waitMs);
    }
}
