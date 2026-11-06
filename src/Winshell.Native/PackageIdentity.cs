using Windows.Win32;
using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>Whether this process runs with an MSIX package identity.</summary>
internal static unsafe class PackageIdentity
{
    /// <summary>Asks for the length of the package's full name, which fails with no identity.</summary>
    internal static bool Exists()
    {
        uint length = 0;
        return PInvoke.GetCurrentPackageFullName(&length, default) != WIN32_ERROR.APPMODEL_ERROR_NO_PACKAGE;
    }
}
