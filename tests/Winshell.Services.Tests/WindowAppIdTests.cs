using Winshell.Native;
using WIN32_ERROR = Windows.Win32.Foundation.WIN32_ERROR;

namespace Winshell.Services.Tests;

/// <summary>The process package id over a fake <c>GetApplicationUserModelId</c>. No process is opened.</summary>
public class WindowAppIdTests
{
    private const string Terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";

    [Fact]
    public void A_package_id_is_read_only_when_the_process_has_one_that_fits()
    {
        Assert.Equal(Terminal, Read(Terminal));
        Assert.Equal("", AppUserModelIds.Read((_, _) =>
            new AppUserModelIds.Query(WIN32_ERROR.APPMODEL_ERROR_NO_APPLICATION, 0)));
        Assert.Equal("", AppUserModelIds.Read((_, _) =>
            new AppUserModelIds.Query(
                WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER,
                (uint)AppUserModelIds.MaxCharacters + 1)));
    }

    private static string Read(string id) =>
        AppUserModelIds.Read((buffer, _) =>
        {
            if (buffer is null)
                return new AppUserModelIds.Query(WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER, (uint)id.Length + 1);
            id.CopyTo(buffer);
            return new AppUserModelIds.Query(WIN32_ERROR.ERROR_SUCCESS, (uint)id.Length + 1);
        });
}
