using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ArrowWheel;

internal sealed class PowerPointNotesScroller
{
    private const string PowerPointProcessName = "POWERPNT";
    private const string PresenterParentClass = "PodiumParent";
    private const string PresenterSurfaceClass = "screenClass";
    private const int MessageTimeoutMilliseconds = 250;
    private static readonly TimeSpan SearchRetryDelay = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan UnavailableLogInterval = TimeSpan.FromSeconds(30);

    private readonly object _sync = new();
    private IntPtr _cachedTarget;
    private long _nextSearchTimestamp;
    private long _nextUnavailableLogTimestamp;

    public bool TryScroll(int delta)
    {
        lock (_sync)
        {
            var target = ResolveTarget();
            if (target == IntPtr.Zero)
            {
                LogUnavailable("presenter-view-not-found");
                return false;
            }

            if (TrySendWheel(target, delta))
            {
                return true;
            }

            var failedTarget = target;
            var firstSendError = Marshal.GetLastWin32Error();
            _cachedTarget = IntPtr.Zero;
            _nextSearchTimestamp = 0;

            target = ResolveTarget();
            if (target != IntPtr.Zero && TrySendWheel(target, delta))
            {
                return true;
            }

            LogUnavailable(
                $"wheel-message-not-delivered; target=0x{failedTarget.ToInt64():X}; " +
                $"win32={firstSendError}");
            return false;
        }
    }

    private IntPtr ResolveTarget()
    {
        if (IsUsableTarget(_cachedTarget))
        {
            return _cachedTarget;
        }

        _cachedTarget = IntPtr.Zero;
        var now = Stopwatch.GetTimestamp();
        if (now < _nextSearchTimestamp)
        {
            return IntPtr.Zero;
        }

        _nextSearchTimestamp = now + (long)(SearchRetryDelay.TotalSeconds * Stopwatch.Frequency);
        var target = FindPresenterSurface();
        if (target == IntPtr.Zero)
        {
            return IntPtr.Zero;
        }

        _cachedTarget = target;
        _nextSearchTimestamp = 0;
        _nextUnavailableLogTimestamp = 0;
        OperationalLog.Write(
            "powerpoint-notes-target",
            $"target=0x{target.ToInt64():X}; class={PresenterSurfaceClass}");
        return target;
    }

    private static IntPtr FindPresenterSurface()
    {
        var bestTarget = IntPtr.Zero;
        long bestArea = 0;

        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window) ||
                !HasClassName(window, PresenterParentClass) ||
                !IsPowerPointWindow(window))
            {
                return true;
            }

            NativeMethods.EnumChildWindows(
                window,
                (child, _) =>
                {
                    if (!NativeMethods.IsWindowVisible(child) ||
                        !HasClassName(child, PresenterSurfaceClass) ||
                        !NativeMethods.GetWindowRect(child, out var rect))
                    {
                        return true;
                    }

                    var width = Math.Max(0, rect.Right - rect.Left);
                    var height = Math.Max(0, rect.Bottom - rect.Top);
                    var area = (long)width * height;
                    if (area > bestArea)
                    {
                        bestArea = area;
                        bestTarget = child;
                    }

                    return true;
                },
                IntPtr.Zero);

            return true;
        }, IntPtr.Zero);

        return bestTarget;
    }

    private static bool IsPowerPointWindow(IntPtr window)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (threadId == 0 || processId == 0)
        {
            return false;
        }

        try
        {
            using var process = Process.GetProcessById(checked((int)processId));
            return string.Equals(
                process.ProcessName,
                PowerPointProcessName,
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (
            exception is ArgumentException or InvalidOperationException or OverflowException or Win32Exception)
        {
            return false;
        }
    }

    private static bool IsUsableTarget(IntPtr target) =>
        target != IntPtr.Zero &&
        NativeMethods.IsWindow(target) &&
        NativeMethods.IsWindowVisible(target) &&
        HasClassName(target, PresenterSurfaceClass) &&
        HasClassName(NativeMethods.GetParent(target), PresenterParentClass);

    private static bool HasClassName(IntPtr window, string expected)
    {
        if (window == IntPtr.Zero)
        {
            return false;
        }

        var className = new char[64];
        var length = NativeMethods.GetClassNameW(window, className, className.Length);
        return length > 0 &&
            expected.AsSpan().SequenceEqual(className.AsSpan(0, length));
    }

    private static bool TrySendWheel(IntPtr target, int delta)
    {
        if (!NativeMethods.GetWindowRect(target, out var rect))
        {
            return false;
        }

        var width = rect.Right - rect.Left;
        var height = rect.Bottom - rect.Top;
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        // 発表者ツールのノートは右下にある。次のスライドを大きくしてノートを
        // 縮めた場合もノート内に残りやすい点を使う。座標は画面座標で渡す。
        var x = rect.Left + (width * 9 / 10);
        var y = rect.Top + (height * 17 / 20);
        var wheelWord = unchecked((ushort)(short)Math.Clamp(delta, short.MinValue, short.MaxValue));
        var wParam = new UIntPtr((uint)wheelWord << 16);
        var packedPoint = unchecked(
            (uint)(ushort)x |
            ((uint)(ushort)y << 16));

        var callResult = NativeMethods.SendMessageTimeoutW(
            target,
            NativeMethods.WmMouseWheel,
            wParam,
            new IntPtr(unchecked((int)packedPoint)),
            NativeMethods.SmtoAbortIfHung,
            MessageTimeoutMilliseconds,
            out _);
        return callResult != IntPtr.Zero;
    }

    private void LogUnavailable(string reason)
    {
        var now = Stopwatch.GetTimestamp();
        if (now < _nextUnavailableLogTimestamp)
        {
            return;
        }

        _nextUnavailableLogTimestamp = now +
            (long)(UnavailableLogInterval.TotalSeconds * Stopwatch.Frequency);
        OperationalLog.Write("powerpoint-notes-unavailable", reason);
    }
}
