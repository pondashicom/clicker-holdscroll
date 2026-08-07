namespace ArrowWheel;

internal sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public bool Enabled { get; set; }
    public int LongPressMilliseconds { get; set; } = 350;
    public int ScrollIntervalMilliseconds { get; set; } = 70;
    public int WheelDelta { get; set; } = 120;
    public int MaxScrollDurationMilliseconds { get; set; } = 30_000;

    public void Normalize()
    {
        LongPressMilliseconds = Math.Clamp(LongPressMilliseconds, 100, 2000);
        ScrollIntervalMilliseconds = Math.Clamp(ScrollIntervalMilliseconds, 20, 1000);
        WheelDelta = Math.Clamp(WheelDelta, 30, 1200);
        MaxScrollDurationMilliseconds = Math.Clamp(MaxScrollDurationMilliseconds, 1000, 120_000);
        SchemaVersion = 1;
    }

    public AppSettings Clone() => new()
    {
        SchemaVersion = SchemaVersion,
        Enabled = Enabled,
        LongPressMilliseconds = LongPressMilliseconds,
        ScrollIntervalMilliseconds = ScrollIntervalMilliseconds,
        WheelDelta = WheelDelta,
        MaxScrollDurationMilliseconds = MaxScrollDurationMilliseconds
    };
}
