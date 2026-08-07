using System.Diagnostics.CodeAnalysis;

namespace ArrowWheel;

internal static class Program
{
    private const string MutexName = @"Local\Clicker-HoldScroll.SingleInstance";

    [STAThread]
    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "プロセス最上位で致命的な起動障害をログとダイアログへ変換する境界です。")]
    private static int Main(string[] args)
    {
        if (args.Contains("--hook-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            return RunHookSmokeTest();
        }

        if (args.Contains("--ui-smoke-test", StringComparer.OrdinalIgnoreCase))
        {
            return RunUiSmokeTest();
        }

        using var mutex = new Mutex(initiallyOwned: true, MutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "Clicker HoldScroll はすでに起動しています。通知領域を確認してください。",
                "Clicker HoldScroll",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return 2;
        }

        ApplicationConfiguration.Initialize();
        try
        {
            using var context = new ArrowWheelApplicationContext();
            Application.Run(context);
        }
        catch (Exception exception)
        {
            OperationalLog.Write("fatal-startup", exception.ToString());
            MessageBox.Show(
                $"Clicker HoldScroll を開始できませんでした。\n\n{exception.Message}\n\nログ: {OperationalLog.LogPath}",
                "Clicker HoldScroll",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }

        return 0;
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "診断モードのプロセス終了コードへすべての起動障害を変換する境界です。")]
    private static int RunHookSmokeTest()
    {
        Exception? hookFailure = null;

        try
        {
            using var hook = new KeyboardHook(
                (_, _, _) => false,
                exception => hookFailure = exception,
                reinstallInterval: TimeSpan.FromMilliseconds(100));
            Thread.Sleep(350);
            return hookFailure is null ? 0 : 1;
        }
        catch (Exception exception)
        {
            OperationalLog.Write("hook-smoke-test-failed", exception.ToString());
            return 1;
        }
    }

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "診断モードのプロセス終了コードへすべてのUI起動障害を変換する境界です。")]
    private static int RunUiSmokeTest()
    {
        try
        {
            ApplicationConfiguration.Initialize();
            using var context = new ArrowWheelApplicationContext(diagnosticMode: true);
            if (!context.SafetyFeaturesAvailable)
            {
                return 1;
            }

            using var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (_, _) => context.ExitThread();
            timer.Start();
            Application.Run(context);
            return 0;
        }
        catch (Exception exception)
        {
            OperationalLog.Write("ui-smoke-test-failed", exception.ToString());
            return 1;
        }
    }
}
