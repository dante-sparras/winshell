namespace Winshell.Input;

/// <summary>Told when Windows drops shortcut capture, or when the foreground window may have changed.</summary>
internal interface IInhibitClient
{
    void Cancelled();

    void Recheck();
}

/// <summary>One key the hook posted to a shell window. Tests read it instead of posting.</summary>
internal readonly record struct PostedKey(nint Hwnd, uint Message, nint WParam, nint LParam);
