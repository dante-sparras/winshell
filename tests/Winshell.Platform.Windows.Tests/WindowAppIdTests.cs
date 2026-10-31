using Winshell.Platform.Windows.Services.WindowList;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// A packaged window with no property-store id uses its process package, and an id set after the first read replaces
/// the program name. The package lookup is a fake. No window is opened.
/// </summary>
public class WindowAppIdTests
{
    private const string Terminal = "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    private const string TerminalExe = @"C:\Program Files\WindowsApps\Microsoft.WindowsTerminal_8wekyb3d8bbwe\WindowsTerminal.exe";

    [Fact]
    public void An_empty_window_property_uses_the_process_package_id()
    {
        var package = Read(Terminal);
        var identity = WindowTracker.Resolve(default, "", package, TerminalExe);

        Assert.Equal(Terminal, identity.AppId);
        Assert.Equal(@"shell:AppsFolder\" + Terminal, identity.Icon);
        Assert.Equal(TerminalExe, identity.Executable);
        Assert.True(identity.PackageKnown);

        var hosted = WindowTracker.Resolve(identity, "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", null, null);
        Assert.Equal("Microsoft.WindowsNotepad_8wekyb3d8bbwe!App", hosted.AppId);
        Assert.Equal(Terminal, hosted.PackageAppId);
    }

    [Fact]
    public void A_window_property_set_after_the_first_describe_replaces_the_program_name()
    {
        var exe = @"C:\Program Files\Google\Chrome\Application\chrome.exe";
        var none = NativeMethods.ReadAppUserModelId((_, _) =>
            new NativeMethods.AppUserModelQuery(NativeMethods.AppModelErrorNoApplication, 0));
        Assert.Equal("", none);

        var first = WindowTracker.Resolve(default, "", none, exe);
        Assert.Equal("chrome", first.AppId);
        Assert.Equal(exe, first.Icon);
        Assert.Equal(exe, first.Executable);

        var second = WindowTracker.Resolve(first, Terminal, null, null);
        Assert.Equal(Terminal, second.AppId);
        Assert.Equal(@"shell:AppsFolder\" + Terminal, second.Icon);
        Assert.Equal(exe, second.Executable);
        Assert.True(second.PackageKnown);
    }

    [Fact]
    public void A_package_id_longer_than_windows_allows_is_ignored()
    {
        var read = NativeMethods.ReadAppUserModelId((_, _) =>
            new NativeMethods.AppUserModelQuery(
                NativeMethods.ErrorInsufficientBuffer,
                (uint)NativeMethods.MaxAppUserModelIdCharacters + 1));
        Assert.Equal("", read);
    }

    private static string Read(string id) =>
        NativeMethods.ReadAppUserModelId((buffer, _) =>
        {
            if (buffer is null)
                return new NativeMethods.AppUserModelQuery(NativeMethods.ErrorInsufficientBuffer, (uint)id.Length + 1);
            id.CopyTo(buffer);
            return new NativeMethods.AppUserModelQuery(NativeMethods.ErrorSuccess, (uint)id.Length + 1);
        });
}
