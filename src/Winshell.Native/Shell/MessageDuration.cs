namespace Winshell.Native;

/// <summary>How long Windows shows a notification, as set in Accessibility.</summary>
internal static unsafe class MessageDuration
{
    /// <summary>The duration in seconds, or null when Windows does not say.</summary>
    internal static uint? Seconds()
    {
        uint seconds = 0;
        return Win32.SystemParametersInfoW(Win32.SPI_GETMESSAGEDURATION, 0, &seconds, 0) ? seconds : null;
    }
}
