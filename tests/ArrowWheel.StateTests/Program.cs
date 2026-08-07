using ArrowWheel;

var failures = new List<string>();

Test("短押しは矢印を再送", () =>
{
    var state = new ArrowPressState();
    Equal(true, state.Press());
    Equal(ReleaseAction.ReplayArrow, state.Release());
});

Test("長押しはスクロールを停止して終了", () =>
{
    var state = new ArrowPressState();
    Equal(true, state.Press());
    Equal(true, state.CrossLongPressThreshold());
    Equal(ReleaseAction.StopScrolling, state.Release());
});

Test("キーリピートを二重押下にしない", () =>
{
    var state = new ArrowPressState();
    Equal(true, state.Press());
    Equal(false, state.Press());
    Equal(ReleaseAction.ReplayArrow, state.Release());
    Equal(ReleaseAction.None, state.Release());
});

Test("離した後の遅延タイマーは無効", () =>
{
    var state = new ArrowPressState();
    state.Press();
    state.Release();
    Equal(false, state.CrossLongPressThreshold());
});

Test("短押しは実入力バックエンドへ矢印を1回送る", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input);
    manager.KeyDown(NativeMethods.VkLeft);
    manager.KeyUp(NativeMethods.VkLeft);
    Equal(1, input.ArrowCount);
    Equal(0, input.WheelCount);
});

Test("長押し中のキーアップでスクロールが停止する", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input);
    manager.KeyDown(NativeMethods.VkRight);
    True(SpinWait.SpinUntil(() => input.WheelCount >= 2, 1000), "scroll did not start");
    manager.KeyUp(NativeMethods.VkRight);
    var stoppedAt = input.WheelCount;
    Thread.Sleep(100);
    Equal(stoppedAt, input.WheelCount);
    Equal(0, input.ArrowCount);
});

Test("PowerPointノートモードは通常ホイールではなくノートへ送る", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input, powerPointNotesMode: true);
    manager.KeyDown(NativeMethods.VkRight);
    True(
        SpinWait.SpinUntil(() => input.PowerPointNotesScrollCount >= 2, 1000),
        "PowerPoint notes scroll did not start");
    manager.KeyUp(NativeMethods.VkRight);
    Equal(0, input.WheelCount);
    Equal(0, input.ArrowCount);
});

Test("PowerPointノートが見つからなくても通常画面へ誤送信しない", () =>
{
    var input = new FakeInputBackend { PowerPointNotesTargetAvailable = false };
    using var manager = CreateManager(input, powerPointNotesMode: true);
    manager.KeyDown(NativeMethods.VkLeft);
    True(
        SpinWait.SpinUntil(() => input.PowerPointNotesScrollCount >= 2, 1000),
        "PowerPoint notes lookup was not attempted");
    manager.KeyUp(NativeMethods.VkLeft);
    Equal(0, input.WheelCount);
    Equal(true, manager.IsInputHealthy);
});

Test("PowerPointノート送信の例外は安全停止へ移行する", () =>
{
    var input = new FakeInputBackend { ThrowOnPowerPointNotesScroll = true };
    var failureCount = 0;
    using var manager = CreateManager(
        input,
        powerPointNotesMode: true,
        onFailure: _ => Interlocked.Increment(ref failureCount));
    manager.KeyDown(NativeMethods.VkRight);
    True(SpinWait.SpinUntil(() => failureCount == 1, 1000), "notes failure was not reported");
    Equal(false, manager.IsInputHealthy);
    Equal(false, manager.IsTracking(NativeMethods.VkRight));
});

Test("物理キーリピート中も一つの長押しとして扱う", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input);
    manager.KeyDown(NativeMethods.VkLeft);
    for (var index = 0; index < 20; index++)
    {
        manager.KeyDown(NativeMethods.VkLeft);
    }

    True(SpinWait.SpinUntil(() => input.WheelCount >= 2, 1000), "scroll did not start");
    for (var index = 0; index < 20; index++)
    {
        manager.KeyDown(NativeMethods.VkLeft);
    }

    Equal(0, input.ArrowCount);
    manager.KeyUp(NativeMethods.VkLeft);
    var stoppedAt = input.WheelCount;
    Thread.Sleep(100);
    Equal(stoppedAt, input.WheelCount);
});

Test("安全上限後はキーを離すまで再始動しない", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input, maxScrollMilliseconds: 1000);
    manager.KeyDown(NativeMethods.VkRight);
    True(SpinWait.SpinUntil(() => input.WheelCount >= 2, 1000), "scroll did not start");
    Thread.Sleep(1200);
    var stoppedAt = input.WheelCount;
    Thread.Sleep(100);
    Equal(stoppedAt, input.WheelCount);
    manager.KeyDown(NativeMethods.VkRight);
    Thread.Sleep(100);
    Equal(stoppedAt, input.WheelCount);
    manager.KeyUp(NativeMethods.VkRight);
    Equal(false, manager.IsTracking(NativeMethods.VkRight));
});

Test("入力失敗時は不健全状態へ移行して再横取りしない", () =>
{
    var input = new FakeInputBackend { ThrowOnArrow = true };
    var failureCount = 0;
    using var manager = CreateManager(input, onFailure: _ => failureCount++);
    manager.KeyDown(NativeMethods.VkLeft);
    manager.KeyUp(NativeMethods.VkLeft);
    Equal(1, failureCount);
    Equal(false, manager.IsInputHealthy);
    manager.KeyDown(NativeMethods.VkLeft);
    Equal(false, manager.IsTracking(NativeMethods.VkLeft));
});

Test("短押し中の修飾キー入力は矢印を先に確定してリリースまで遮断する", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input);
    manager.KeyDown(NativeMethods.VkLeft);
    manager.ModifierPressed();
    Equal(1, input.ArrowCount);
    Equal(true, manager.IsTracking(NativeMethods.VkLeft));
    manager.KeyDown(NativeMethods.VkLeft);
    Equal(1, input.ArrowCount);
    manager.KeyUp(NativeMethods.VkLeft);
    Equal(false, manager.IsTracking(NativeMethods.VkLeft));
});

Test("長押し中の修飾キー入力は追加入力なしで停止する", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input);
    manager.KeyDown(NativeMethods.VkRight);
    True(SpinWait.SpinUntil(() => input.WheelCount >= 2, 1000), "scroll did not start");
    manager.ModifierPressed();
    var stoppedAt = input.WheelCount;
    Thread.Sleep(100);
    Equal(stoppedAt, input.WheelCount);
    Equal(0, input.ArrowCount);
    manager.KeyUp(NativeMethods.VkRight);
});

Test("短押し1000回で取りこぼしを起こさない", () =>
{
    var input = new FakeInputBackend();
    using var manager = CreateManager(input);
    for (var index = 0; index < 1000; index++)
    {
        manager.KeyDown(NativeMethods.VkLeft);
        manager.KeyUp(NativeMethods.VkLeft);
    }

    Equal(1000, input.ArrowCount);
});

Test("ホイール再送失敗時も不健全状態へ移行する", () =>
{
    var input = new FakeInputBackend { ThrowOnWheel = true };
    var failureCount = 0;
    using var manager = CreateManager(input, onFailure: _ => Interlocked.Increment(ref failureCount));
    manager.KeyDown(NativeMethods.VkLeft);
    True(SpinWait.SpinUntil(() => failureCount == 1, 1000), "wheel failure was not reported");
    Equal(false, manager.IsInputHealthy);
    Equal(false, manager.IsTracking(NativeMethods.VkLeft));
});

Test("設定がない場合は無効状態で開始する", () =>
{
    WithTemporarySettingsPath(path =>
    {
        var result = SettingsStore.LoadFrom(path);
        Equal(false, result.Settings.Enabled);
        True(!string.IsNullOrWhiteSpace(result.Warning), "missing settings warning was not returned");
    });
});

Test("破損設定を退避して無効状態へ戻す", () =>
{
    WithTemporarySettingsPath(path =>
    {
        File.WriteAllText(path, "{ invalid json");
        var result = SettingsStore.LoadFrom(path);
        Equal(false, result.Settings.Enabled);
        Equal(false, File.Exists(path));
        Equal(1, Directory.GetFiles(Path.GetDirectoryName(path)!, "settings.corrupt.*.json").Length);
    });
});

Test("設定更新はバックアップを残して置換する", () =>
{
    WithTemporarySettingsPath(path =>
    {
        SettingsStore.SaveTo(path, new AppSettings { Enabled = true });
        SettingsStore.SaveTo(path, new AppSettings { Enabled = false });
        Equal(true, File.Exists(path));
        Equal(true, File.Exists(path + ".bak"));
        Equal(false, SettingsStore.LoadFrom(path).Settings.Enabled);
        Equal(0, Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp").Length);
    });
});

Test("Clicker-PONの旧設定を新名称の保存先へ移行する", () =>
{
    WithTemporarySettingsPath(path =>
    {
        var root = Path.GetDirectoryName(path)!;
        var legacyPath = Path.Combine(root, "legacy", "settings.json");
        var newPath = Path.Combine(root, "new", "settings.json");
        SettingsStore.SaveTo(legacyPath, new AppSettings { Enabled = true });
        var result = SettingsStore.LoadWithMigration(newPath, legacyPath);
        Equal(true, result.Settings.Enabled);
        Equal(true, File.Exists(newPath));
        True(result.Warning?.Contains("移行", StringComparison.Ordinal) == true,
            "migration warning was not returned");
    });
});

Test("PowerPointノートモードを設定に保存して復元する", () =>
{
    WithTemporarySettingsPath(path =>
    {
        SettingsStore.SaveTo(path, new AppSettings
        {
            Enabled = true,
            PowerPointNotesMode = true
        });
        var loaded = SettingsStore.LoadFrom(path).Settings;
        Equal(true, loaded.Enabled);
        Equal(true, loaded.PowerPointNotesMode);
        Equal(2, loaded.SchemaVersion);
    });
});

Test("旧スキーマ設定ではPowerPointノートモードを無効で移行する", () =>
{
    WithTemporarySettingsPath(path =>
    {
        File.WriteAllText(
            path,
            "{\"SchemaVersion\":1,\"Enabled\":true," +
            "\"LongPressMilliseconds\":350,\"ScrollIntervalMilliseconds\":70," +
            "\"WheelDelta\":120,\"MaxScrollDurationMilliseconds\":30000}");
        var loaded = SettingsStore.LoadFrom(path).Settings;
        Equal(false, loaded.PowerPointNotesMode);
        Equal(2, loaded.SchemaVersion);
    });
});

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine("PASS: state, routing, safety-limit, settings, migration, and stress 22 tests");
return 0;

void Test(string name, Action action)
{
    try
    {
        action();
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL: {name}: {exception.Message}");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"expected={expected}, actual={actual}");
    }
}

static void True(bool actual, string message)
{
    if (!actual)
    {
        throw new InvalidOperationException(message);
    }
}

static ArrowHoldManager CreateManager(
    FakeInputBackend input,
    int maxScrollMilliseconds = 2000,
    bool powerPointNotesMode = false,
    Action<Exception>? onFailure = null)
{
    var settings = new AppSettings
    {
        LongPressMilliseconds = 100,
        ScrollIntervalMilliseconds = 20,
        WheelDelta = 120,
        MaxScrollDurationMilliseconds = maxScrollMilliseconds,
        PowerPointNotesMode = powerPointNotesMode
    };
    return new ArrowHoldManager(settings, input, onFailure ?? (_ => { }));
}

static void WithTemporarySettingsPath(Action<string> action)
{
    var directory = Path.Combine(
        Path.GetTempPath(),
        "Clicker-HoldScroll.Tests",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);

    try
    {
        action(Path.Combine(directory, "settings.json"));
    }
    finally
    {
        Directory.Delete(directory, recursive: true);
    }
}
