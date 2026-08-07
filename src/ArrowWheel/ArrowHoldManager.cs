using System.ComponentModel;
using System.Diagnostics;

namespace ArrowWheel;

internal sealed class ArrowHoldManager : IDisposable
{
    private sealed class Hold
    {
        public required uint VirtualKey { get; init; }
        public required int WheelDirection { get; init; }
        public required long StartedTimestamp { get; init; }
        public ArrowPressState State { get; } = new();
        public System.Threading.Timer? Timer { get; set; }
    }

    private readonly object _sync = new();
    private readonly Dictionary<uint, Hold> _holds = new();
    private readonly HashSet<uint> _blockedUntilRelease = new();
    private readonly IInputBackend _input;
    private readonly Action<Exception> _inputFailureHandler;
    private AppSettings _settings;
    private bool _disposed;
    private bool _inputHealthy = true;

    public ArrowHoldManager(
        AppSettings settings,
        IInputBackend input,
        Action<Exception> inputFailureHandler)
    {
        _settings = settings.Clone();
        _settings.Normalize();
        _input = input;
        _inputFailureHandler = inputFailureHandler;
    }

    public bool IsInputHealthy
    {
        get
        {
            lock (_sync)
            {
                return _inputHealthy;
            }
        }
    }

    public bool IsTracking(uint virtualKey)
    {
        lock (_sync)
        {
            return _holds.ContainsKey(virtualKey) || _blockedUntilRelease.Contains(virtualKey);
        }
    }

    public void UpdateSettings(AppSettings settings)
    {
        var snapshot = settings.Clone();
        snapshot.Normalize();

        lock (_sync)
        {
            _settings = snapshot;
        }
    }

    public void ResetInputHealth()
    {
        lock (_sync)
        {
            _inputHealthy = true;
        }
    }

    public void KeyDown(uint virtualKey)
    {
        lock (_sync)
        {
            if (_disposed || !_inputHealthy || _holds.ContainsKey(virtualKey) ||
                _blockedUntilRelease.Contains(virtualKey))
            {
                return;
            }

            var hold = new Hold
            {
                VirtualKey = virtualKey,
                WheelDirection = virtualKey == NativeMethods.VkLeft ? 1 : -1,
                StartedTimestamp = Stopwatch.GetTimestamp()
            };
            hold.State.Press();
            hold.Timer = new System.Threading.Timer(
                BeginLongPress,
                hold,
                _settings.LongPressMilliseconds,
                Timeout.Infinite);
            _holds.Add(virtualKey, hold);
        }
    }

    public void KeyUp(uint virtualKey)
    {
        Hold? hold;
        ReleaseAction action;

        lock (_sync)
        {
            if (_blockedUntilRelease.Remove(virtualKey))
            {
                return;
            }

            if (!_holds.Remove(virtualKey, out hold))
            {
                return;
            }

            hold.Timer?.Dispose();
            action = hold.State.Release();
        }

        if (action == ReleaseAction.ReplayArrow)
        {
            TrySend(() => _input.SendArrowPress(virtualKey));
        }
    }

    public void ModifierPressed()
    {
        List<uint> arrowsToReplay = [];

        lock (_sync)
        {
            foreach (var hold in _holds.Values)
            {
                hold.Timer?.Dispose();
                if (hold.State.Release() == ReleaseAction.ReplayArrow)
                {
                    arrowsToReplay.Add(hold.VirtualKey);
                }

                _blockedUntilRelease.Add(hold.VirtualKey);
            }

            _holds.Clear();
        }

        foreach (var virtualKey in arrowsToReplay)
        {
            TrySend(() => _input.SendArrowPress(virtualKey));
        }
    }

    private void BeginLongPress(object? state)
    {
        if (state is not Hold hold)
        {
            return;
        }

        var sendFailed = false;

        lock (_sync)
        {
            if (_disposed || !_inputHealthy ||
                !_holds.TryGetValue(hold.VirtualKey, out var current) ||
                !ReferenceEquals(current, hold) || !hold.State.CrossLongPressThreshold())
            {
                return;
            }

            hold.Timer?.Dispose();
            hold.Timer = new System.Threading.Timer(
                RepeatScroll,
                hold,
                _settings.ScrollIntervalMilliseconds,
                _settings.ScrollIntervalMilliseconds);
            var delta = hold.WheelDirection * _settings.WheelDelta;
            sendFailed = !TryScroll(delta);
        }

        if (sendFailed)
        {
            CancelAll();
        }
    }

    private void RepeatScroll(object? state)
    {
        if (state is not Hold hold)
        {
            return;
        }

        var sendFailed = false;

        lock (_sync)
        {
            if (_disposed || !_inputHealthy ||
                !_holds.TryGetValue(hold.VirtualKey, out var current) ||
                !ReferenceEquals(current, hold) || !hold.State.IsLongPress)
            {
                return;
            }

            var elapsed = Stopwatch.GetElapsedTime(hold.StartedTimestamp);
            if (elapsed.TotalMilliseconds >= _settings.MaxScrollDurationMilliseconds)
            {
                _holds.Remove(hold.VirtualKey);
                hold.Timer?.Dispose();
                _blockedUntilRelease.Add(hold.VirtualKey);
            }
            else
            {
                var delta = hold.WheelDirection * _settings.WheelDelta;
                sendFailed = !TryScroll(delta);
            }
        }

        if (sendFailed)
        {
            CancelAll();
        }
    }

    private bool TrySend(Action send)
    {
        try
        {
            send();
            return true;
        }
        catch (Exception exception) when (exception is Win32Exception or InvalidOperationException)
        {
            lock (_sync)
            {
                _inputHealthy = false;
            }

            _inputFailureHandler(exception);
            return false;
        }
    }

    private bool TryScroll(int delta) => TrySend(() =>
    {
        if (_settings.PowerPointNotesMode)
        {
            _input.TryScrollPowerPointNotes(delta);
            return;
        }

        _input.SendMouseWheel(delta);
    });

    public void CancelAll()
    {
        lock (_sync)
        {
            foreach (var hold in _holds.Values)
            {
                hold.Timer?.Dispose();
                hold.State.Reset();
            }

            _holds.Clear();
            _blockedUntilRelease.Clear();
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
        }

        CancelAll();
    }
}
