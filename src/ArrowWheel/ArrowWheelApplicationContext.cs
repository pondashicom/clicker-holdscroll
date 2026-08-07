using System.ComponentModel;
using System.Drawing;
using Microsoft.Win32;

namespace ArrowWheel;

internal sealed class ArrowWheelApplicationContext : ApplicationContext
{
    private readonly Control _uiInvoker;
    private readonly AppSettings _settings;
    private readonly ArrowHoldManager _holdManager;
    private readonly NotifyIcon _notifyIcon;
    private readonly ContextMenuStrip _contextMenu;
    private readonly ToolStripMenuItem _enabledMenuItem;
    private readonly ToolStripMenuItem _powerPointNotesModeMenuItem;
    private readonly bool _diagnosticMode;
    private KeyboardHook? _keyboardHook;
    private EmergencyHotkeyWindow? _emergencyHotkey;
    private volatile bool _enabled;
    private volatile bool _resourcesDisposed;

    public ArrowWheelApplicationContext(bool diagnosticMode = false)
    {
        _diagnosticMode = diagnosticMode;
        _uiInvoker = new Control();
        _uiInvoker.CreateControl();

        var loadResult = SettingsStore.Load();
        _settings = loadResult.Settings;
        _enabled = _settings.Enabled;
        _holdManager = new ArrowHoldManager(
            _settings,
            new WindowsInputBackend(),
            OnInputFailure);

        _enabledMenuItem = new ToolStripMenuItem("有効", null, (_, _) => ToggleEnabled())
        {
            Checked = _enabled,
            CheckOnClick = false
        };
        _powerPointNotesModeMenuItem = new ToolStripMenuItem(
            "PowerPointノートモード",
            null,
            (_, _) => TogglePowerPointNotesMode())
        {
            Checked = _settings.PowerPointNotesMode,
            CheckOnClick = false
        };
        _contextMenu = BuildMenu();
        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = StatusText(),
            ContextMenuStrip = _contextMenu,
            Visible = !diagnosticMode
        };
        _notifyIcon.DoubleClick += (_, _) => ToggleEnabled();

        try
        {
            _emergencyHotkey = new EmergencyHotkeyWindow(
                () => EmergencyStop("緊急停止ホットキー", showBalloon: true));
        }
        catch (Win32Exception exception)
        {
            _enabled = false;
            _settings.Enabled = false;
            _enabledMenuItem.Checked = false;
            OperationalLog.Write("hotkey-registration-failed", exception.Message);
            ShowBalloon(
                ToolTipIcon.Warning,
                "安全機能の警告",
                "Ctrl+Shift+F12 を登録できなかったため、無効状態で開始しました。");
        }

        try
        {
            _keyboardHook = new KeyboardHook(HandleKey, OnHookFailure);
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }
        catch
        {
            DisposeResources();
            throw;
        }

        OperationalLog.Write(
            "startup",
            $"enabled={_enabled}; powerpointNotesMode={_settings.PowerPointNotesMode}; " +
            $"version={Application.ProductVersion}");
        if (!string.IsNullOrWhiteSpace(loadResult.Warning))
        {
            OperationalLog.Write("settings-warning", loadResult.Warning);
            ShowBalloon(ToolTipIcon.Info, "安全状態で開始", loadResult.Warning);
        }
    }

    public bool SafetyFeaturesAvailable => _keyboardHook is not null && _emergencyHotkey is not null;

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledMenuItem);
        menu.Items.Add(_powerPointNotesModeMenuItem);
        menu.Items.Add(new ToolStripSeparator());

        var thresholdMenu = new ToolStripMenuItem("長押し判定");
        AddChoice(thresholdMenu, "速い（250 ms）", 250, () => _settings.LongPressMilliseconds,
            value => _settings.LongPressMilliseconds = value);
        AddChoice(thresholdMenu, "標準（350 ms）", 350, () => _settings.LongPressMilliseconds,
            value => _settings.LongPressMilliseconds = value);
        AddChoice(thresholdMenu, "ゆっくり（500 ms）", 500, () => _settings.LongPressMilliseconds,
            value => _settings.LongPressMilliseconds = value);
        menu.Items.Add(thresholdMenu);

        var speedMenu = new ToolStripMenuItem("スクロール速度");
        AddChoice(speedMenu, "速い", 40, () => _settings.ScrollIntervalMilliseconds,
            value => _settings.ScrollIntervalMilliseconds = value);
        AddChoice(speedMenu, "標準", 70, () => _settings.ScrollIntervalMilliseconds,
            value => _settings.ScrollIntervalMilliseconds = value);
        AddChoice(speedMenu, "ゆっくり", 120, () => _settings.ScrollIntervalMilliseconds,
            value => _settings.ScrollIntervalMilliseconds = value);
        menu.Items.Add(speedMenu);

        var safetyLimitMenu = new ToolStripMenuItem("連続スクロール安全上限");
        AddChoice(safetyLimitMenu, "10秒", 10_000, () => _settings.MaxScrollDurationMilliseconds,
            value => _settings.MaxScrollDurationMilliseconds = value);
        AddChoice(safetyLimitMenu, "30秒（標準）", 30_000, () => _settings.MaxScrollDurationMilliseconds,
            value => _settings.MaxScrollDurationMilliseconds = value);
        AddChoice(safetyLimitMenu, "60秒", 60_000, () => _settings.MaxScrollDurationMilliseconds,
            value => _settings.MaxScrollDurationMilliseconds = value);
        menu.Items.Add(safetyLimitMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("緊急停止（Ctrl+Shift+F12）", null,
            (_, _) => EmergencyStop("トレイメニュー", showBalloon: true));
        menu.Items.Add("ログフォルダーを開く", null, (_, _) => OpenLogFolder());
        menu.Items.Add("使い方", null, (_, _) => ShowHelp());
        menu.Items.Add("終了", null, (_, _) => ExitThread());
        return menu;
    }

    private void AddChoice(
        ToolStripMenuItem parent,
        string text,
        int value,
        Func<int> getValue,
        Action<int> setValue)
    {
        var item = new ToolStripMenuItem(text)
        {
            Checked = getValue() == value
        };
        item.Click += (_, _) =>
        {
            setValue(value);
            foreach (var sibling in parent.DropDownItems.OfType<ToolStripMenuItem>())
            {
                sibling.Checked = ReferenceEquals(sibling, item);
            }

            _holdManager.UpdateSettings(_settings);
            SaveSettings();
        };
        parent.DropDownItems.Add(item);
    }

    private bool HandleKey(uint virtualKey, bool isKeyDown, bool isInjected)
    {
        if (isInjected)
        {
            return false;
        }

        if (isKeyDown && NativeMethods.IsModifierKey(virtualKey))
        {
            _holdManager.ModifierPressed();
            return false;
        }

        if (!NativeMethods.IsArrowKey(virtualKey))
        {
            return false;
        }

        if (_holdManager.IsTracking(virtualKey))
        {
            if (isKeyDown)
            {
                _holdManager.KeyDown(virtualKey);
            }
            else
            {
                _holdManager.KeyUp(virtualKey);
            }

            return true;
        }

        if (!_enabled || !_holdManager.IsInputHealthy || !isKeyDown ||
            NativeMethods.IsAnyModifierPressed())
        {
            return false;
        }

        _holdManager.KeyDown(virtualKey);
        return _holdManager.IsTracking(virtualKey);
    }

    private void ToggleEnabled()
    {
        if (_enabled)
        {
            EmergencyStop("ユーザー操作", showBalloon: false);
            return;
        }

        if (_emergencyHotkey is null)
        {
            ShowBalloon(
                ToolTipIcon.Error,
                "有効化できません",
                "緊急停止ホットキーを使用できないため、安全上の理由で有効化を拒否しました。");
            return;
        }

        _holdManager.ResetInputHealth();
        _enabled = true;
        _settings.Enabled = true;
        UpdateStatus();
        SaveSettings();
        OperationalLog.Write("enabled", "user");
    }

    private void TogglePowerPointNotesMode()
    {
        _settings.PowerPointNotesMode = !_settings.PowerPointNotesMode;
        _powerPointNotesModeMenuItem.Checked = _settings.PowerPointNotesMode;
        _holdManager.UpdateSettings(_settings);
        UpdateStatus();
        SaveSettings();
        OperationalLog.Write(
            "powerpoint-notes-mode",
            _settings.PowerPointNotesMode ? "enabled" : "disabled");

        var message = _settings.PowerPointNotesMode
            ? "長押しは、起動中のPowerPoint発表者ツールのノートだけをスクロールします。"
            : "長押しは、通常のマウスホイールとして動作します。";
        ShowBalloon(ToolTipIcon.Info, "PowerPointノートモード", message);
    }

    private void EmergencyStop(string reason, bool showBalloon)
    {
        if (_uiInvoker.InvokeRequired)
        {
            PostToUi(() => EmergencyStop(reason, showBalloon));
            return;
        }

        _enabled = false;
        _settings.Enabled = false;
        _holdManager.CancelAll();
        UpdateStatus();
        SaveSettings();
        OperationalLog.Write("emergency-stop", reason);

        if (showBalloon)
        {
            ShowBalloon(ToolTipIcon.Warning, "Clicker HoldScroll を停止", "入力変換を無効にしました。");
        }
    }

    private void OnInputFailure(Exception exception)
    {
        PostToUi(() =>
        {
            OperationalLog.Write("input-failure", exception.ToString());
            EmergencyStop("入力再送失敗", showBalloon: false);
            ShowBalloon(
                ToolTipIcon.Error,
                "入力変換エラー",
                "入力再送に失敗したため、自動的に無効化しました。ログを確認してください。");
        });
    }

    private void OnHookFailure(Exception exception)
    {
        PostToUi(() =>
        {
            OperationalLog.Write("hook-failure", exception.ToString());
            EmergencyStop("キーボードフック障害", showBalloon: false);
            ShowBalloon(
                ToolTipIcon.Error,
                "キーボード監視エラー",
                "キーボード監視に失敗したため、自動的に無効化しました。再起動してください。");
        });
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs eventArgs)
    {
        if (eventArgs.Mode == PowerModes.Suspend)
        {
            PostToUi(() => EmergencyStop("システムのサスペンド", showBalloon: false));
        }
    }

    private void OnSessionSwitch(object sender, SessionSwitchEventArgs eventArgs)
    {
        if (eventArgs.Reason is SessionSwitchReason.SessionLock or
            SessionSwitchReason.RemoteDisconnect or
            SessionSwitchReason.ConsoleDisconnect)
        {
            PostToUi(() => EmergencyStop($"セッション変更: {eventArgs.Reason}", showBalloon: false));
        }
    }

    private void PostToUi(Action action)
    {
        if (_resourcesDisposed || _uiInvoker.IsDisposed || !_uiInvoker.IsHandleCreated)
        {
            return;
        }

        try
        {
            _uiInvoker.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
            // 終了処理との競合。すでに安全停止へ向かっているため無視する。
        }
    }

    private void UpdateStatus()
    {
        _enabledMenuItem.Checked = _enabled;
        _powerPointNotesModeMenuItem.Checked = _settings.PowerPointNotesMode;
        _notifyIcon.Text = StatusText();
    }

    private string StatusText()
    {
        if (!_enabled)
        {
            return "Clicker HoldScroll（無効）";
        }

        return _settings.PowerPointNotesMode
            ? "Clicker HoldScroll（有効・PPTノート）"
            : "Clicker HoldScroll（有効）";
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(_settings);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            OperationalLog.Write("settings-save-failure", exception.ToString());
            ShowBalloon(ToolTipIcon.Error, "設定保存エラー", "設定を保存できませんでした。ログを確認してください。");
        }
    }

    private void OpenLogFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(OperationalLog.LogPath)!;
            Directory.CreateDirectory(directory);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = directory,
                UseShellExecute = true
            });
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or Win32Exception)
        {
            OperationalLog.Write("open-log-folder-failure", exception.ToString());
            ShowBalloon(ToolTipIcon.Error, "ログ表示エラー", "ログフォルダーを開けませんでした。");
        }
    }

    private static void ShowHelp()
    {
        MessageBox.Show(
            "短押し\n" +
            "  ← / → を通常の矢印キーとして1回入力します。\n\n" +
            "長押し\n" +
            "  ← は上へ、→ は下へ連続スクロールします。\n\n" +
            "PowerPointノートモード\n" +
            "  起動中のPowerPoint発表者ツールを探し、長押しでノートだけをスクロールします。\n" +
            "  発表者ツールが見つからない場合は、別の画面をスクロールしません。\n\n" +
            "安全機能\n" +
            "  Ctrl+Shift+F12 でいつでも緊急停止できます。\n" +
            "  修飾キー併用、画面ロック、スリープ、入力障害時は安全停止します。\n\n" +
            "通知領域アイコンのダブルクリックで有効／無効を切り替えます。",
            "Clicker HoldScroll の使い方",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowBalloon(ToolTipIcon icon, string title, string message)
    {
        if (!_diagnosticMode && !_resourcesDisposed)
        {
            _notifyIcon.ShowBalloonTip(5000, title, message, icon);
        }
    }

    protected override void ExitThreadCore()
    {
        DisposeResources();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeResources();
        }

        base.Dispose(disposing);
    }

    private void DisposeResources()
    {
        if (_resourcesDisposed)
        {
            return;
        }

        _resourcesDisposed = true;
        _enabled = false;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        _keyboardHook?.Dispose();
        _keyboardHook = null;
        _holdManager.Dispose();
        _emergencyHotkey?.Dispose();
        _emergencyHotkey = null;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _contextMenu.Dispose();
        _enabledMenuItem.Dispose();
        _uiInvoker.Dispose();
        OperationalLog.Write("shutdown", "normal");
    }
}
