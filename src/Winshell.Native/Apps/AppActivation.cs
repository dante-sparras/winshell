using System.Runtime.InteropServices;

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
        object? created = null;
        try
        {
            var type = Type.GetTypeFromCLSID(Clsid);
            created = type is null ? null : Activator.CreateInstance(type);
            if (created is not IApplicationActivationManager manager)
                throw new InvalidOperationException("The activation manager is not available.");
            manager.ActivateApplication(appUserModelId, arguments, 0, out _);
        }
        finally
        {
            if (created is not null && Marshal.IsComObject(created))
                Marshal.ReleaseComObject(created);
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
