using Winshell.Diagnostics;

namespace Winshell.Commands;

internal static class DoctorCommand
{
    /// <summary>winshell doctor: checks this PC's setup, prints a line each, and fails when a check fails. It never builds.</summary>
    internal static int Run(CliOptions options)
    {
        var checks = Doctor.Run(new DoctorContext(ShellLocations.Current, options.Path, options.Config));
        if (options.Json)
        {
            Console.WriteLine(DoctorReport.ToJson(checks, CliMetadata.Version));
        }
        else
        {
            var color = !options.NoColor && !Console.IsOutputRedirected &&
                        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
            Console.Write(DoctorReport.Format(checks, CliMetadata.Version, color));
        }

        return checks.Any(c => c.Status == CheckStatus.Fail) ? 1 : 0;
    }
}
