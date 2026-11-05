namespace Winshell.Native;

/// <summary>Whether a contrast theme is on.</summary>
internal static unsafe class ContrastTheme
{
    internal static bool IsOn()
    {
        var hc = new HIGHCONTRASTW { cbSize = (uint)sizeof(HIGHCONTRASTW) };
        return Win32.SystemParametersInfoW(Win32.SPI_GETHIGHCONTRAST, hc.cbSize, &hc, 0) &&
               (hc.dwFlags & Win32.HCF_HIGHCONTRASTON) != 0;
    }
}
