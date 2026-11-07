using System.Runtime.InteropServices;
using Windows.Win32.System.Com;

namespace Winshell.Native;

/// <summary>Starts a packaged app by its AppUserModelID, the way Start does.</summary>
internal static class AppActivation
{
    private static readonly Guid Clsid = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");

    /// <summary>
    /// Returns once the app is up, seconds on a cold start, so call it on its own STA thread. Throws when Windows
    /// cannot activate the app.
    /// </summary>
    internal static void Activate(string appUserModelId, string arguments)
    {
        var manager = Com.Create<IApplicationActivationManager>(Clsid, CLSCTX.CLSCTX_SERVER, out var hr);
        if (manager is null)
            throw new InvalidOperationException($"The activation manager is not available (0x{hr:X8}).");
        try
        {
            manager.ActivateApplication(appUserModelId, arguments, 0, out _);
        }
        finally
        {
            Com.Release(manager);
        }
    }

    /// <summary>
    /// Handwritten: CsWin32's struct also declares <c>ActivateForFile</c>, which pulls in <c>IShellItem</c> and
    /// <c>IBindCtx</c>, whose generated monikers use the banned <c>System.Runtime.InteropServices.ComTypes</c>.
    /// </summary>
    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
    }
}
