namespace ArrowWheel;

internal enum ReleaseAction
{
    None,
    ReplayArrow,
    StopScrolling
}

internal sealed class ArrowPressState
{
    public bool IsPressed { get; private set; }
    public bool IsLongPress { get; private set; }

    public bool Press()
    {
        if (IsPressed)
        {
            return false;
        }

        IsPressed = true;
        IsLongPress = false;
        return true;
    }

    public bool CrossLongPressThreshold()
    {
        if (!IsPressed || IsLongPress)
        {
            return false;
        }

        IsLongPress = true;
        return true;
    }

    public ReleaseAction Release()
    {
        if (!IsPressed)
        {
            return ReleaseAction.None;
        }

        var action = IsLongPress ? ReleaseAction.StopScrolling : ReleaseAction.ReplayArrow;
        IsPressed = false;
        IsLongPress = false;
        return action;
    }

    public void Reset()
    {
        IsPressed = false;
        IsLongPress = false;
    }
}
