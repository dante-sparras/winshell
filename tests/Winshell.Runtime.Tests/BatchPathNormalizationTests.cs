using System.Diagnostics;
using Winshell.Runtime.Shell;

namespace Winshell.Runtime.Tests;

public sealed class BatchPathNormalizationTests
{
    [Theory]
    [InlineData("run.cmd.")]
    [InlineData("run.cmd ")]
    [InlineData("run.bat. ")]
    public void Batch_detection_normalizes_a_path_even_before_the_file_exists(string filename)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), filename);
        var spec = new ProcessSpec([path, "safe&second.cmd"], new Dictionary<string, string?>(), false, null);

        var start = ProcessLauncher.CreateStartInfo(spec, redirect: false);

        Assert.Equal("cmd.exe", Path.GetFileName(start.FileName), ignoreCase: true);
        Assert.Empty(start.ArgumentList);
        Assert.Contains("\"safe&second.cmd\"", start.Arguments);
    }

    [Theory]
    [InlineData(".")]
    [InlineData(" ")]
    [InlineData(". ")]
    public void A_batch_path_with_a_trailing_dot_or_space_keeps_arguments_quoted(string suffix)
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var script = Path.Combine(directory, "args.cmd");
            var output = Path.Combine(directory, "args.txt");
            File.WriteAllText(script, "@echo %1> \"%~dp0args.txt\"\r\n");
            File.WriteAllText(Path.Combine(directory, "second.cmd"), "@echo ran> \"%~dp0second.txt\"\r\n");
            var spec = new ProcessSpec([script + suffix, "safe&second.cmd"], new Dictionary<string, string?>(), false, directory);

            using var process = Process.Start(ProcessLauncher.CreateStartInfo(spec, redirect: false))!;
            Assert.True(process.WaitForExit(10_000));

            Assert.Equal("\"safe&second.cmd\"", File.ReadAllText(output).TrimEnd());
            Assert.False(File.Exists(Path.Combine(directory, "second.txt")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
