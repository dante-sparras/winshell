#pragma warning disable CA1416
using Winshell.Tray;

namespace Winshell.Services.Tests;

public sealed class TrayIconSettingsTests
{
    [Fact]
    public void Overflow_settings_match_by_guid_or_by_program_and_id()
    {
        var guid = Guid.NewGuid();
        List<TrayIconSetting> settings =
        [
            new(guid, "", null, Promoted: true),
            new(Guid.Empty, @"C:\Apps\sync.exe", 1, Promoted: true),
            new(Guid.Empty, @"C:\Apps\sync.exe", 2, Promoted: false),
        ];
        Assert.False(TrayIconSettings.InOverflow(settings, guid, @"C:\Other\app.exe", 9));
        Assert.False(TrayIconSettings.InOverflow(settings, Guid.Empty, @"c:\apps\SYNC.exe", 1));
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.Empty, @"C:\Apps\sync.exe", 2));
        // Unknown: a new icon, which Windows 11 puts in the flyout.
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.Empty, @"C:\Apps\sync.exe", 3));
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.NewGuid(), @"C:\Apps\sync.exe", 1));
        // An icon with a GUID is not matched by program, nor a program by a GUID entry.
        Assert.True(TrayIconSettings.InOverflow([new(Guid.Empty, @"C:\Apps\sync.exe", 1, true)], Guid.NewGuid(), @"C:\Apps\sync.exe", 1));
        Assert.True(TrayIconSettings.InOverflow([new(guid, @"C:\Apps\sync.exe", 1, true)], Guid.Empty, @"C:\Apps\sync.exe", 1));
    }

    [Fact]
    public void Windows_settings_read_with_known_folders_expanded()
    {
        // Read-only: the choices Explorer has stored for this user.
        var settings = TrayIconSettings.Read();
        Assert.All(settings.Where(s => s.ExecutablePath.Length > 0), s => Assert.False(s.ExecutablePath.StartsWith('{'), s.ExecutablePath));
    }
}
