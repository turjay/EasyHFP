using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using EasyHFP.Interop;

namespace EasyHFP.UI
{
    public class HotkeyWindow : NativeWindow, IDisposable
    {
        public const int HOTKEY_ID = 9001;

        public event Action? HotkeyPressed;
        public event Action<string>? RegistrationFailed;

        private bool _isRegistered = false;

        public bool IsRegistered => _isRegistered;

        public HotkeyWindow(string hotkeyString)
        {
            CreateHandle(new CreateParams());
            RegisterFromConfig(hotkeyString);
        }

        public void RegisterFromConfig(string hotkeyString)
        {
            if (_isRegistered)
            {
                NativeUser32.UnregisterHotKey(Handle, HOTKEY_ID);
                _isRegistered = false;
            }

            if (string.IsNullOrWhiteSpace(hotkeyString))
            {
                return;
            }

            uint modifiers = NativeUser32.MOD_NOREPEAT;
            Keys key = Keys.None;

            string[] parts = hotkeyString.Split('+');
            foreach (var rawPart in parts)
            {
                string part = rawPart.Trim();
                if (part.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) || part.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeUser32.MOD_CONTROL;
                }
                else if (part.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeUser32.MOD_ALT;
                }
                else if (part.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeUser32.MOD_SHIFT;
                }
                else if (part.Equals("Win", StringComparison.OrdinalIgnoreCase) || part.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeUser32.MOD_WIN;
                }
                else
                {
                    if (Enum.TryParse<Keys>(part, true, out var parsedKey))
                    {
                        key = parsedKey;
                    }
                }
            }

            if (key == Keys.None)
            {
                RegistrationFailed?.Invoke($"Could not parse key '{hotkeyString}'.");
                return;
            }

            bool success = NativeUser32.RegisterHotKey(Handle, HOTKEY_ID, modifiers, (uint)key);
            if (success)
            {
                _isRegistered = true;
            }
            else
            {
                int err = Marshal.GetLastWin32Error();
                RegistrationFailed?.Invoke($"Failed to register hotkey '{hotkeyString}' (Win32 Error: {err}). It may already be in use by another program.");
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeUser32.WM_HOTKEY && m.WParam.ToInt32() == HOTKEY_ID)
            {
                HotkeyPressed?.Invoke();
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (_isRegistered)
            {
                NativeUser32.UnregisterHotKey(Handle, HOTKEY_ID);
                _isRegistered = false;
            }
            DestroyHandle();
        }
    }
}
