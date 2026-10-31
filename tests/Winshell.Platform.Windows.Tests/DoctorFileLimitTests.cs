using Winshell.Platform.Windows.Diagnostics;

namespace Winshell.Platform.Windows.Tests;

/// <summary>
/// Doctor's per-file byte budget. The files are a few dozen bytes and the budget passed in is 40, so this does not
/// allocate a 1 MiB document.
/// </summary>
public class DoctorFileLimitTests
{
    [Fact]
    public void A_qml_file_past_the_byte_budget_is_named_and_not_parsed()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-doctor-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var shell = Path.Combine(dir, "shell.qml");
            File.WriteAllText(shell, "import Winshell\nShellRoot {}\n");
            File.WriteAllText(Path.Combine(dir, "Big.qml"), new string('x', 80));
            var sub = Path.Combine(dir, "sub");
            Directory.CreateDirectory(sub);
            var ok = Path.Combine(sub, "Ok.qml");
            File.WriteAllText(ok, "import QtQuick\nItem {}\n");
            var big = Path.Combine(sub, "Big.qml");
            File.WriteAllText(big, new string('x', 80));
            Assert.InRange(new FileInfo(shell).Length, 1, 40);
            Assert.InRange(new FileInfo(ok).Length, 1, 40);

            var scan = DoctorProbe.ParseConfig(shell, 40);

            Assert.Equal(2, scan.Files);
            var rootError = Assert.Single(scan.Errors);
            Assert.Contains(Path.Combine(dir, "Big.qml"), rootError, StringComparison.Ordinal);
            Assert.Contains("above the limit", rootError, StringComparison.Ordinal);
            var nested = Assert.Single(scan.SubfolderErrors);
            Assert.Contains(big, nested, StringComparison.Ordinal);
            Assert.Contains("above the limit", nested, StringComparison.Ordinal);
            Assert.False(scan.Stopped);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
