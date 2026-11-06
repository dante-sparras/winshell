namespace Winshell.Tests;

/// <summary>
/// A configuration's instances are its Run build and its Dev build. A program whose folder only shares a prefix
/// with <c>bin\shell</c> belongs to neither.
/// </summary>
public sealed class ConfigurationInstancesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void A_run_build_and_a_dev_build_belong_and_a_shared_prefix_does_not()
    {
        var folder = Path.Combine(_root, "MyProject");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "Bar.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        var project = ConfigurationProject.Find(folder, null, folder, out var error);
        Assert.Null(error);
        Assert.NotNull(project);

        var members = ConfigurationInstances.Of([
            Instance("run", Path.Combine(folder, "bin", "shell", "Bar.exe")),
            Instance("dev", Path.Combine(folder, "bin", "shell.next", "Bar.exe")),
            Instance("extra", Path.Combine(folder, "bin", "shell.extra", "Bar.exe"))
        ], project!);

        Assert.Equal(["run", "dev"], members.Select(member => member.Info.Id));
        Assert.Equal(ConfigurationInstances.Build.Run, members[0].Build);
        Assert.Equal(ConfigurationInstances.Build.Dev, members[1].Build);
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

    private static InstanceInfo Instance(string id, string program)
    {
        return new InstanceInfo(id, 1, "shell", program, DateTime.UnixEpoch, "pipe-" + id, "log-" + id);
    }
}
