using System.Runtime.InteropServices;
using System.Text;

namespace Winshell;

/// <summary>The code page a console program prints in when it sets none of its own.</summary>
internal static class ConsolePage
{
    private static Encoding? _encoding;

    /// <summary>
    /// The shell's console code page, such as 850. A program started with no window gets a console of its own, and that
    /// console starts in this page whatever the desktop itself was started from. Safe from any thread.
    /// </summary>
    internal static Encoding Encoding => _encoding ??= Read();

    private static Encoding Read()
    {
        // .NET reads only Unicode and Latin-1 until the other code pages are registered. Registering twice is harmless.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding((int)GetOEMCP());
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetOEMCP();
}
