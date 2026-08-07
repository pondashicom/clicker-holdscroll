using System.Text;

namespace ArrowWheel;

internal static class OperationalLog
{
    private const long MaxLogBytes = 1_048_576;
    private static readonly object Sync = new();

    public static string LogPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Clicker-HoldScroll",
        "logs",
        "clicker-holdscroll.log");

    public static void Write(string eventName, string message)
    {
        try
        {
            lock (Sync)
            {
                var directory = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(directory);
                RotateIfNeeded();
                var sanitizedMessage = message.ReplaceLineEndings(" ");
                var line = $"{DateTimeOffset.Now:O}\t{eventName}\t{sanitizedMessage}{Environment.NewLine}";
                File.AppendAllText(LogPath, line, new UTF8Encoding(false));
            }
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // ログ障害で入力処理を停止させない。
        }
    }

    private static void RotateIfNeeded()
    {
        if (!File.Exists(LogPath) || new FileInfo(LogPath).Length < MaxLogBytes)
        {
            return;
        }

        var previousPath = LogPath + ".1";
        File.Move(LogPath, previousPath, overwrite: true);
    }
}
