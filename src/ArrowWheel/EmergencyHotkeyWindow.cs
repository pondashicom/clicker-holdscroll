using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ArrowWheel;

internal sealed class EmergencyHotkeyWindow : NativeWindow, IDisposable
{
    private const int HotkeyId = 0x4350;
    private bool _registered;
    private bool _disposed;

    public EmergencyHotkeyWindow(Action pressed)
    {
        Pressed = pressed;
        CreateHandle(new CreateParams
        {
            Caption = "Clicker HoldScroll Emergency Hotkey"
        });

        _registered = NativeMethods.RegisterHotKey(
            Handle,
            HotkeyId,
            NativeMethods.ModControl | NativeMethods.ModShift | NativeMethods.ModNoRepeat,
            NativeMethods.VkF12);

        if (!_registered)
        {
            var error = Marshal.GetLastWin32Error();
            DestroyHandle();
            throw new Win32Exception(error, "緊急停止ホットキー Ctrl+Shift+F12 を登録できませんでした。");
        }
    }

    private Action Pressed { get; }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == NativeMethods.WmHotKey && message.WParam.ToInt32() == HotkeyId)
        {
            Pressed();
            return;
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_registered)
        {
            NativeMethods.UnregisterHotKey(Handle, HotkeyId);
            _registered = false;
        }

        DestroyHandle();
    }
}
