using Microsoft.Win32;
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
            new(guid, "", null, true),
            new(Guid.Empty, @"C:\Apps\sync.exe", 1, true),
            new(Guid.Empty, @"C:\Apps\sync.exe", 2, false)
        ];
        Assert.False(TrayIconSettings.InOverflow(settings, guid, @"C:\Other\app.exe", 9));
        Assert.False(TrayIconSettings.InOverflow(settings, Guid.Empty, @"c:\apps\SYNC.exe", 1));
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.Empty, @"C:\Apps\sync.exe", 2));
        // Unknown: a new icon, which Windows 11 puts in the flyout.
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.Empty, @"C:\Apps\sync.exe", 3));
        Assert.True(TrayIconSettings.InOverflow(settings, Guid.NewGuid(), @"C:\Apps\sync.exe", 1));
        // An icon with a GUID is not matched by program, nor a program by a GUID entry.
        Assert.True(TrayIconSettings.InOverflow([new TrayIconSetting(Guid.Empty, @"C:\Apps\sync.exe", 1, true)],
            Guid.NewGuid(), @"C:\Apps\sync.exe", 1));
        Assert.True(TrayIconSettings.InOverflow([new TrayIconSetting(guid, @"C:\Apps\sync.exe", 1, true)], Guid.Empty,
            @"C:\Apps\sync.exe", 1));
    }

    [Fact]
    public void Explorer_s_keys_read_as_choices_with_known_folders_expanded()
    {
        // As Explorer writes HKCU\Control Panel\NotifyIconSettings, in a key of the test's own.
        var path = @"Software\Winshell\Tests\" + Guid.NewGuid().ToString("N");
        var guid = Guid.NewGuid();
        try
        {
            using (var root = Registry.CurrentUser.CreateSubKey(path))
            {
                using (var byGuid = root.CreateSubKey("1"))
                {
                    byGuid.SetValue("IconGuid", guid.ToString("B"));
                    byGuid.SetValue("IsPromoted", 1, RegistryValueKind.DWord);
                }

                using var byProgram = root.CreateSubKey("2");
                byProgram.SetValue("ExecutablePath", @"{1AC14E77-02E7-4E5D-B744-2EB1AE5198B7}\notepad.exe");
                byProgram.SetValue("UID", unchecked((int)0x80000001), RegistryValueKind.DWord);
            }

            using var key = Registry.CurrentUser.OpenSubKey(path)!;
            var settings = TrayIconSettings.Read(key).OrderBy(s => s.Guid == Guid.Empty).ToList();

            Assert.Equal(new TrayIconSetting(guid, "", null, true), settings[0]);
            Assert.Equal(Guid.Empty, settings[1].Guid);
            Assert.Equal(Path.Combine(Environment.SystemDirectory, "notepad.exe"), settings[1].ExecutablePath,
                StringComparer.OrdinalIgnoreCase);
            Assert.Equal(0x80000001u, settings[1].Id);
            Assert.False(settings[1].Promoted);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(path, false);
        }
    }
}
