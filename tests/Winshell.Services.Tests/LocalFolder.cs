using System.Runtime.CompilerServices;

namespace Winshell.Services.Tests;

/// <summary>Every test here runs with a local folder of its own, so it never joins the user's instances.</summary>
internal static class LocalFolder
{
    [ModuleInitializer]
    internal static void Set() =>
        Environment.SetEnvironmentVariable("WINSHELL_LOCAL_DIR", Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N")));
}
