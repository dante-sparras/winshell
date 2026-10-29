using System.Diagnostics;
using System.Text;

namespace Winshell.Tests;

/// <summary>
/// Package zips sort entry names with ordinal comparison. Lithuanian sorts "y" after "į"; ordinal does not.
/// </summary>
public class ZipEntryOrderTests
{
    [Fact]
    public void Zip_entries_sort_by_ordinal_under_lithuanian()
    {
        var package = Path.Combine(RepoRoot(), "scripts", "package.ps1");
        var probe = Path.Combine(Path.GetTempPath(), "winshell-zip-order-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(probe, """
            $ErrorActionPreference = 'Stop'
            $env:WINSHELL_PACKAGE_TEST = '1'
            . $env:WINSHELL_PACKAGE_SCRIPT
            [Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('lt-LT')
            $files = @(
                [pscustomobject]@{ Name = 'y' }
                [pscustomobject]@{ Name = 'i' }
                [pscustomobject]@{ Name = [string][char]0x012F }
            )
            $sorted = Sort-WinshellZipEntry -Files $files
            (($sorted | ForEach-Object { [int][char]$_.Name }) -join ',')
            """, new UTF8Encoding(false));
        try
        {
            var info = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-File");
            info.ArgumentList.Add(probe);
            info.Environment["WINSHELL_PACKAGE_SCRIPT"] = package;
            using var process = Process.Start(info);
            Assert.NotNull(process);
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            Assert.True(process.ExitCode == 0, stdout + stderr);
            Assert.Equal("105,121,303", stdout.Trim());
        }
        finally
        {
            File.Delete(probe);
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "winshell.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("The tests run outside the repository.");
    }
}
