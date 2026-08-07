namespace ArrowWheel;

internal interface IInputBackend
{
    bool IsAnyModifierPressed();
    void SendArrowPress(uint virtualKey);
    void SendMouseWheel(int delta);
    bool TryScrollPowerPointNotes(int delta);
}

internal sealed class WindowsInputBackend : IInputBackend
{
    private readonly PowerPointNotesScroller _powerPointNotesScroller = new();

    public bool IsAnyModifierPressed() => NativeMethods.IsAnyModifierPressed();

    public void SendArrowPress(uint virtualKey)
    {
        if (IsAnyModifierPressed())
        {
            throw new InvalidOperationException(
                "修飾キーが押されているため、安全のため矢印キーの再送を中止しました。");
        }

        NativeMethods.SendArrowPress(virtualKey);
    }

    public void SendMouseWheel(int delta) => NativeMethods.SendMouseWheel(delta);

    public bool TryScrollPowerPointNotes(int delta) =>
        _powerPointNotesScroller.TryScroll(delta);
}
