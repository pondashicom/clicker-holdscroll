namespace ArrowWheel;

internal static class NativeMethods
{
    public const uint VkLeft = 0x25;
    public const uint VkRight = 0x27;
}

internal interface IInputBackend
{
    bool IsAnyModifierPressed();
    void SendArrowPress(uint virtualKey);
    void SendMouseWheel(int delta);
    bool TryScrollPowerPointNotes(int delta);
}

internal sealed class FakeInputBackend : IInputBackend
{
    private int _arrowCount;
    private int _wheelCount;
    private int _powerPointNotesScrollCount;

    public int ArrowCount => Volatile.Read(ref _arrowCount);
    public int WheelCount => Volatile.Read(ref _wheelCount);
    public int PowerPointNotesScrollCount => Volatile.Read(ref _powerPointNotesScrollCount);
    public bool ThrowOnArrow { get; set; }
    public bool ThrowOnWheel { get; set; }
    public bool ThrowOnPowerPointNotesScroll { get; set; }
    public bool PowerPointNotesTargetAvailable { get; set; } = true;

    public bool IsAnyModifierPressed() => false;

    public void SendArrowPress(uint virtualKey)
    {
        if (ThrowOnArrow)
        {
            throw new InvalidOperationException("simulated arrow failure");
        }

        Interlocked.Increment(ref _arrowCount);
    }

    public void SendMouseWheel(int delta)
    {
        if (ThrowOnWheel)
        {
            throw new InvalidOperationException("simulated wheel failure");
        }

        Interlocked.Increment(ref _wheelCount);
    }

    public bool TryScrollPowerPointNotes(int delta)
    {
        if (ThrowOnPowerPointNotesScroll)
        {
            throw new InvalidOperationException("simulated PowerPoint notes scroll failure");
        }

        Interlocked.Increment(ref _powerPointNotesScrollCount);
        return PowerPointNotesTargetAvailable;
    }
}
