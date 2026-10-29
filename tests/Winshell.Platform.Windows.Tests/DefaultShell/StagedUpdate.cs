using Winshell.Platform.Windows.Services.Updates;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests.DefaultShell;

/// <summary>
/// An updater for a portable Winshell 0.5.0 in a temporary folder of its own, with an update already downloaded, or
/// none. GitHub cannot be reached, so a check fails, and a restart is only counted.
/// </summary>
internal sealed class StagedUpdate : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-staged-update-tests", Guid.NewGuid().ToString("N"));

    public StagedUpdate(string? version)
    {
        var install = Path.Combine(_root, "install");
        var updates = Path.Combine(_root, "updates");
        Directory.CreateDirectory(install);
        if (version is not null)
        {
            Directory.CreateDirectory(Path.Combine(updates, version));
            File.WriteAllText(Path.Combine(updates, version, "winshell.exe"), "new");
        }
        Updater = new Updater("dante-sparras/winshell", install, updates, "0.5.0", "win-x64", new Log(), new Offline())
        {
            Restart = () =>
            {
                if (Refusal is { } refusal)
                    return refusal;
                Restarts++;
                return null;
            },
        };
    }

    public Updater Updater { get; }

    /// <summary>Restarts the host accepted.</summary>
    public int Restarts { get; private set; }

    /// <summary>Why the host refuses to restart, as when another Winshell runs; null accepts.</summary>
    public string? Refusal { get; set; }

    public void Dispose()
    {
        Updater.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    private sealed class Offline : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("No such host is known.");
    }
}
