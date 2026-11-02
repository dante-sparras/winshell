namespace Winshell.Services.Tests;

/// <summary>What a <see cref="TomlFile"/> test runs in: a temporary folder, a loop only the test turns, and a log it can read.</summary>
internal sealed class TomlFixture : IDisposable
{
    public TomlFixture(string? text = null, string name = "settings.toml")
    {
        Directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        FilePath = Path.Combine(Directory, name);
        Log = new Log { Sink = (_, _, message) => Messages.Add(message) };
        if (text is not null)
            File.WriteAllText(FilePath, text);
    }

    public string Directory { get; }

    public string FilePath { get; }

    public ManualEventLoop Loop { get; } = new();

    public Log Log { get; }

    public List<string> Messages { get; } = [];

    public string Text => File.ReadAllText(FilePath);

    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }
}
