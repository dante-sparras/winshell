using Winshell.Platform.Windows;

namespace Winshell.Platform.Windows.Tests;

/// <summary>A broken TOML config file uses the same notice as a QML error, including the line and the file itself.</summary>
public class TomlConfigNoticeTests
{
    [Fact]
    public void A_toml_error_names_the_line_and_opens_that_file()
    {
        var dir = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var root = Path.Combine(dir, "shell.qml");
            var prefs = Path.Combine(dir, "preferences.toml");
            File.WriteAllText(root, "");
            File.WriteAllText(prefs, "x");
            var error = prefs + "[4:2]: Invalid character";

            var notice = ShellNotices.ForConfigError(error, root, running: true);

            Assert.Equal("preferences.toml, line 4, column 2", notice.Detail);
            Assert.Equal(prefs, notice.Target);
            Assert.Equal("Invalid character", notice.Body);
            Assert.Equal("Open file", notice.ActionText);
            Assert.Equal(prefs, ShellNotices.ConfigFileOrRoot(prefs, root));
        }
        finally
        {
            try
            {
                Directory.Delete(dir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
