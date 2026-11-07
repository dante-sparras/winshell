using System.Text.Json;

namespace Winshell.Tests;

/// <summary>
/// <c>log</c> may show a stopped instance, and a selector never adopts a different one. The instances are folders
/// <see cref="Instances.List"/> reads, so this does not start a shell.
/// </summary>
public sealed class LogFallbackTests : IDisposable
{
    private const string StoppedId = "abc123stopped";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
    private readonly string _run;

    public LogFallbackTests()
    {
        _run = Path.Combine(_root, "run");
        Write(StoppedId, Path.Combine(_root, "MyShell.exe"), int.MaxValue - 1, DateTime.Now);
    }

    [Theory]
    [InlineData("--pid", "1234", "No instance with process id 1234.")]
    [InlineData("-i", "deadbeef", "No instance with id deadbeef.")]
    public void A_selector_that_matches_nothing_does_not_take_the_stopped_instance(string option, string value,
        string message)
    {
        var choice = Ask("log", option, value);

        Assert.Null(choice.Info);
        Assert.Equal(1, choice.Code);
        Assert.Equal(message, choice.Error);
    }

    [Fact]
    public void A_selector_names_a_stopped_instance()
    {
        var choice = Ask("log", "-i", StoppedId[..6]);

        Assert.Equal(StoppedId, choice.Info!.Id);
        Assert.Equal($"Showing the log of stopped instance {StoppedId}.", choice.Notice);
    }

    [Fact]
    public void With_no_selector_the_stopped_instance_is_named()
    {
        var choice = Ask("log");

        Assert.Equal(StoppedId, choice.Info!.Id);
        Assert.Equal($"Showing the log of stopped instance {StoppedId}.", choice.Notice);
    }

    [Fact]
    public void A_project_selector_finds_a_stopped_instance_beside_the_build_folder()
    {
        var project = Path.Combine(_root, "MyProject");
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Bar.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var program = Path.Combine(project, "bin", "shell.next", "Bar.exe");
        // Older than the other stopped instance, so only -p can choose it. A missing selector shows the newer log.
        Write("besideabc", program, int.MaxValue - 2, DateTime.Now.AddHours(-2));

        var choice = Ask("log", "-p", project);

        Assert.Equal("besideabc", choice.Info!.Id);
        Assert.Equal("Showing the log of stopped instance besideabc.", choice.Notice);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }

    private InstanceSelection.Result Ask(params string[] args)
    {
        return InstanceSelection.Select(CliParser.Parse(args), InstanceSelection.Question.Log,
            new Instances(_run).List(true));
    }

    private void Write(string id, string configPath, int pid, DateTime launched)
    {
        var dir = Path.Combine(_run, id);
        Directory.CreateDirectory(dir);
        var log = Path.Combine(dir, "log.txt");
        File.WriteAllText(log, id + "\n");
        var info = new InstanceInfo(id, pid, "stopped", configPath, launched, "winshell-" + id, log,
            1, DateTime.Now.AddMinutes(-30));
        File.WriteAllText(Path.Combine(dir, "instance.json"),
            JsonSerializer.Serialize(info, InstanceJson.Default.InstanceInfo));
    }
}
