using System.Text;
using System.Text.Json;

namespace ArrowWheel;

internal sealed record SettingsLoadResult(AppSettings Settings, string? Warning);

internal static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string SettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Clicker-HoldScroll",
        "settings.json");

    private static string LegacySettingsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Clicker-PON",
        "settings.json");

    public static SettingsLoadResult Load() =>
        LoadWithMigration(SettingsPath, LegacySettingsPath);

    internal static SettingsLoadResult LoadWithMigration(
        string settingsPath,
        string legacySettingsPath)
    {
        if (File.Exists(settingsPath) || !File.Exists(legacySettingsPath))
        {
            return LoadFrom(settingsPath);
        }

        var legacyResult = LoadFrom(legacySettingsPath);
        try
        {
            SaveTo(settingsPath, legacyResult.Settings);
            var warning = string.IsNullOrWhiteSpace(legacyResult.Warning)
                ? "Clicker-PON の旧設定を Clicker HoldScroll へ移行しました。"
                : $"{legacyResult.Warning} Clicker HoldScroll の保存先へ安全設定を作成しました。";
            return new SettingsLoadResult(legacyResult.Settings, warning);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var warning = string.IsNullOrWhiteSpace(legacyResult.Warning)
                ? "Clicker-PON の旧設定を読み込みましたが、新しい保存先へ移行できませんでした。"
                : $"{legacyResult.Warning} 新しい保存先へ移行できませんでした。";
            return new SettingsLoadResult(legacyResult.Settings, warning);
        }
    }

    internal static SettingsLoadResult LoadFrom(string settingsPath)
    {
        if (!File.Exists(settingsPath))
        {
            return new SettingsLoadResult(
                new AppSettings { Enabled = false },
                "初回起動のため、安全のため無効状態で開始しました。");
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                    File.ReadAllText(settingsPath, Encoding.UTF8))
                ?? throw new JsonException("設定ファイルの内容が空です。");
            settings.Normalize();
            return new SettingsLoadResult(settings, null);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            var quarantinedPath = TryQuarantineCorruptSettings(settingsPath);
            var location = quarantinedPath is null
                ? "破損ファイルは退避できませんでした。"
                : $"破損ファイルは {quarantinedPath} に退避しました。";

            return new SettingsLoadResult(
                new AppSettings { Enabled = false },
                $"設定を読み込めなかったため、安全のため無効状態で開始しました。{location}");
        }
    }

    public static void Save(AppSettings settings) => SaveTo(SettingsPath, settings);

    internal static void SaveTo(string settingsPath, AppSettings settings)
    {
        var snapshot = settings.Clone();
        snapshot.Normalize();

        var directory = Path.GetDirectoryName(settingsPath)!;
        Directory.CreateDirectory(directory);
        var temporaryPath = Path.Combine(
            directory,
            $"settings.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            File.WriteAllText(
                temporaryPath,
                JsonSerializer.Serialize(snapshot, JsonOptions),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (File.Exists(settingsPath))
            {
                File.Replace(
                    temporaryPath,
                    settingsPath,
                    settingsPath + ".bak",
                    ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, settingsPath);
            }
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? TryQuarantineCorruptSettings(string settingsPath)
    {
        try
        {
            var quarantinePath = Path.Combine(
                Path.GetDirectoryName(settingsPath)!,
                $"settings.corrupt.{DateTimeOffset.Now:yyyyMMdd-HHmmssfff}.json");
            File.Move(settingsPath, quarantinePath);
            return quarantinePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
