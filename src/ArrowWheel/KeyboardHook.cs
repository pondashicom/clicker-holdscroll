using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace ArrowWheel;

internal sealed class KeyboardHook : IDisposable
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ReinstallInterval = TimeSpan.FromSeconds(30);

    private readonly NativeMethods.LowLevelKeyboardProc _callback;
    private readonly Func<uint, bool, bool, bool> _keyHandler;
    private readonly Action<Exception> _faultHandler;
    private readonly ManualResetEventSlim _started = new(false);
    private readonly Thread _hookThread;
    private System.Threading.Timer? _reinstallTimer;
    private Exception? _startupException;
    private IntPtr _hookHandle;
    private uint _hookThreadId;
    private int _disposed;

    public KeyboardHook(
        Func<uint, bool, bool, bool> keyHandler,
        Action<Exception> faultHandler,
        TimeSpan? reinstallInterval = null)
    {
        _keyHandler = keyHandler;
        _faultHandler = faultHandler;
        _callback = HookCallback;
        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "Clicker HoldScroll Keyboard Hook"
        };
        _hookThread.Start();

        if (!_started.Wait(StartupTimeout))
        {
            Dispose();
            throw new TimeoutException("キーボードフック用スレッドの開始がタイムアウトしました。");
        }

        if (_startupException is not null)
        {
            Dispose();
            throw new InvalidOperationException("キーボードフックを開始できませんでした。", _startupException);
        }

        var effectiveReinstallInterval = reinstallInterval ?? ReinstallInterval;
        _reinstallTimer = new System.Threading.Timer(
            RequestReinstall,
            null,
            effectiveReinstallInterval,
            effectiveReinstallInterval);
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "バックグラウンドスレッドから未処理例外を流出させず安全停止へ通知する境界です。")]
    private void HookThreadMain()
    {
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            NativeMethods.PeekMessageW(
                out _,
                IntPtr.Zero,
                0,
                0,
                NativeMethods.PmNoRemove);
            InstallHook();
            _started.Set();

            while (true)
            {
                var result = NativeMethods.GetMessageW(out var message, IntPtr.Zero, 0, 0);
                if (result == 0)
                {
                    break;
                }

                if (result == -1)
                {
                    throw new Win32Exception(
                        Marshal.GetLastWin32Error(),
                        "フック用メッセージループに失敗しました。");
                }

                if (message.Id == NativeMethods.WmAppReinstallHook)
                {
                    if (Volatile.Read(ref _disposed) != 0)
                    {
                        continue;
                    }

                    try
                    {
                        ReinstallHook();
                    }
                    catch (Exception exception)
                    {
                        _faultHandler(exception);
                    }

                    continue;
                }

                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessageW(ref message);
            }
        }
        catch (Exception exception)
        {
            if (!_started.IsSet)
            {
                _startupException = exception;
                _started.Set();
            }
            else if (Volatile.Read(ref _disposed) == 0)
            {
                _faultHandler(exception);
            }
        }
        finally
        {
            UninstallHook();
            _started.Set();
        }
    }

    private void InstallHook()
    {
        var moduleHandle = NativeMethods.GetModuleHandleW(null);
        var handle = NativeMethods.SetWindowsHookExW(
            NativeMethods.WhKeyboardLl,
            _callback,
            moduleHandle,
            0);

        if (handle == IntPtr.Zero)
        {
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "キーボードフックの登録に失敗しました。");
        }

        _hookHandle = handle;
    }

    private void ReinstallHook()
    {
        var previousHandle = _hookHandle;
        InstallHook();

        if (previousHandle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(previousHandle);
        }
    }

    private void RequestReinstall(object? _)
    {
        if (Volatile.Read(ref _disposed) != 0 || _hookThreadId == 0)
        {
            return;
        }

        if (!NativeMethods.PostThreadMessageW(
                _hookThreadId,
                NativeMethods.WmAppReinstallHook,
                UIntPtr.Zero,
                IntPtr.Zero))
        {
            _faultHandler(new Win32Exception(
                Marshal.GetLastWin32Error(),
                "キーボードフックの再登録要求に失敗しました。"));
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "例外をアンマネージドのフック境界へ流出させないために必要です。")]
    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        try
        {
            if (code >= 0)
            {
                var data = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                var message = wParam.ToInt32();
                var isKeyDown = message is NativeMethods.WmKeyDown or NativeMethods.WmSysKeyDown;
                var isKeyUp = message is NativeMethods.WmKeyUp or NativeMethods.WmSysKeyUp;

                if ((isKeyDown || isKeyUp) &&
                    _keyHandler(data.VirtualKeyCode, isKeyDown, NativeMethods.IsInjected(data)))
                {
                    return new IntPtr(1);
                }
            }
        }
        catch (Exception exception)
        {
            _faultHandler(exception);
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private void UninstallHook()
    {
        var handle = Interlocked.Exchange(ref _hookHandle, IntPtr.Zero);
        if (handle != IntPtr.Zero)
        {
            NativeMethods.UnhookWindowsHookEx(handle);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _reinstallTimer?.Dispose();
        _reinstallTimer = null;

        if (_hookThreadId != 0)
        {
            NativeMethods.PostThreadMessageW(
                _hookThreadId,
                NativeMethods.WmQuit,
                UIntPtr.Zero,
                IntPtr.Zero);
        }

        var threadStopped = !_hookThread.IsAlive;
        if (!threadStopped && Thread.CurrentThread != _hookThread)
        {
            threadStopped = _hookThread.Join(TimeSpan.FromSeconds(3));
        }

        UninstallHook();
        if (threadStopped)
        {
            _started.Dispose();
        }

        GC.KeepAlive(_callback);
    }
}
