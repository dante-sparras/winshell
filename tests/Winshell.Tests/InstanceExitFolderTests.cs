using System.Text.Json;
using Winshell;

namespace Winshell.Tests;

/// <summary>A clean exit keeps the instance folder, and a later start deletes it once it is a day old.</summary>
public sealed class InstanceExitFolderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));

    public InstanceExitFolderTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void A_clean_exit_keeps_the_folder_and_is_not_listed_as_running()
    {
        var id = "abc123abc123";
        var dir = Path.Combine(_root, id);
        Directory.CreateDirectory(dir);
        var info = new InstanceInfo(id, 0, "shell", "C:\\cfg\\shell.qml", DateTime.Now, "winshell-" + id, Path.Combine(dir, "log.txt"), 1);
        File.WriteAllText(Path.Combine(dir, "instance.json"), JsonSerializer.Serialize(info, InstanceJson.Default.InstanceInfo));
        File.WriteAllText(info.Log, "hello");

        Instances.MarkExited(_root, id);

        Assert.True(File.Exists(Path.Combine(dir, "instance.json")));
        Assert.Equal("hello", File.ReadAllText(info.Log));
        var written = JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(dir, "instance.json")), InstanceJson.Default.InstanceInfo);
        Assert.NotNull(written!.ExitedAt);
        Assert.DoesNotContain(Instances.ListIn(_root, false), row => row.Info.Id == id);
        Assert.Contains(Instances.ListIn(_root, true), row => row.Info.Id == id && !row.Alive);
    }

    [Fact]
    public void Clean_deletes_an_old_exit_and_an_old_folder_with_no_instance_file()
    {
        var oldExit = Path.Combine(_root, "oldexitoldexi");
        Directory.CreateDirectory(oldExit);
        var exited = new InstanceInfo("oldexitoldexi", 0, "shell", "C:\\cfg\\shell.qml", DateTime.Now.AddDays(-3), "pipe", Path.Combine(oldExit, "log.txt"), 1, DateTime.Now.AddDays(-2));
        File.WriteAllText(Path.Combine(oldExit, "instance.json"), JsonSerializer.Serialize(exited, InstanceJson.Default.InstanceInfo));

        var young = Path.Combine(_root, "youngeyoungex");
        Directory.CreateDirectory(young);
        var fresh = new InstanceInfo("youngeyoungex", 0, "shell", "C:\\cfg\\shell.qml", DateTime.Now, "pipe", Path.Combine(young, "log.txt"), 1, DateTime.Now);
        File.WriteAllText(Path.Combine(young, "instance.json"), JsonSerializer.Serialize(fresh, InstanceJson.Default.InstanceInfo));

        var orphan = Path.Combine(_root, "orphanorphan");
        Directory.CreateDirectory(orphan);
        var orphanLog = Path.Combine(orphan, "log.txt");
        File.WriteAllText(orphanLog, "old");
        File.SetLastWriteTime(orphanLog, DateTime.Now.AddDays(-2));

        var recentOrphan = Path.Combine(_root, "recentorphan");
        Directory.CreateDirectory(recentOrphan);
        File.WriteAllText(Path.Combine(recentOrphan, "log.txt"), "new");

        Instances.CleanIn(_root);

        Assert.False(Directory.Exists(oldExit));
        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(young));
        Assert.True(Directory.Exists(recentOrphan));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch (IOException)
        {
        }
    }
}
