using System.IO.Pipes;
using System.Security.Principal;
using System.Text;
using Winshell.Runtime;

namespace Winshell.Tests;

public class PipeTrustTests
{
    private const int Low = 0x1000;
    private const int Medium = 0x2000;
    private const int High = 0x3000;

    [Theory]
    [InlineData(@"C:\Program Files\winshell\winshell.exe", true)]
    [InlineData(@"C:\Program Files\winshell\winshellw.exe", true)]
    [InlineData(@"C:\Program Files\winshell\WINSHELL.EXE", true)]
    [InlineData(@"C:\Program Files\winshell\testhost.exe", false)]
    [InlineData(@"C:\Program Files\winshell\notwinshell.exe", false)]
    [InlineData(@"C:\Program Files\winshell\winshell.exe.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_shell_images_are_trusted(string? path, bool trusted)
    {
        Assert.Equal(trusted, PipeTrust.IsWinshellImage(path));
    }

    [Fact]
    public void A_lower_integrity_is_refused_and_an_unknown_level_fails_closed()
    {
        Assert.False(PipeTrust.AtLeast(Low, Medium));
        Assert.True(PipeTrust.AtLeast(Medium, Medium));
        Assert.True(PipeTrust.AtLeast(High, Medium));
        Assert.False(PipeTrust.AtLeast(0, Medium));
        Assert.False(PipeTrust.AtLeast(Medium, 0));
    }

    [Fact]
    public void The_running_process_has_an_image_and_an_integrity_level()
    {
        var path = PipeTrust.ImagePath(Environment.ProcessId);
        Assert.False(string.IsNullOrEmpty(path));
        Assert.False(PipeTrust.IsWinshellImage(path));
        Assert.True(PipeTrust.IntegrityRid(Environment.ProcessId) >= Medium);
    }

    [Fact]
    public void The_first_pipe_is_medium_labeled_and_a_second_first_instance_fails()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        using var first = IpcServer.OpenFirst(name);
        var label = PipeTrust.MandatoryLabel(first.SafePipeHandle);
        Assert.Contains("ME", label, StringComparison.Ordinal);
        Assert.Contains("NW", label, StringComparison.Ordinal);
        Assert.Throws<IOException>(() => IpcServer.OpenFirst(name));
    }

    [Fact]
    public void The_pipe_is_owned_by_the_user_even_when_elevated()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        using var first = IpcServer.OpenFirst(name);
        var owner = first.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
        Assert.Equal(WindowsIdentity.GetCurrent().User, owner);
    }

    [Fact]
    public void A_taken_name_listens_on_a_new_id()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        using var held = IpcServer.OpenFirst(name);
        var chosen = name;
        using var other = IpcServer.OpenAvailable(ref chosen);
        Assert.NotEqual(name, chosen);
        Assert.StartsWith("winshell-", chosen, StringComparison.Ordinal);
        Assert.Contains("ME", PipeTrust.MandatoryLabel(other.SafePipeHandle), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_later_instance_keeps_the_medium_label_and_accepts_another_client()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        await using var first = IpcServer.OpenFirst(name);
        var firstWait = first.WaitForConnectionAsync();
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        await firstWait;
        await using var second = IpcServer.OpenNext(name);
        var label = PipeTrust.MandatoryLabel(second.SafePipeHandle);
        Assert.Contains("ME", label, StringComparison.Ordinal);
        Assert.Contains("NW", label, StringComparison.Ordinal);
        var secondWait = second.WaitForConnectionAsync();
        await using var next = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await next.ConnectAsync(3000);
        await secondWait;
        Assert.True(PipeTrust.ClientMayUseShell(second.SafePipeHandle));
    }

    [Fact]
    public async Task A_connected_client_of_this_process_is_at_the_shell_integrity()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        await using var server = IpcServer.OpenFirst(name);
        var waiting = server.WaitForConnectionAsync();
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        await waiting;
        Assert.True(PipeTrust.ClientMayUseShell(server.SafePipeHandle));
    }

    [Fact]
    public async Task A_pipe_owned_by_another_image_is_refused()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        await using var server = IpcServer.OpenFirst(name);
        var waiting = server.WaitForConnectionAsync();
        var error = await Assert.ThrowsAsync<IOException>(async () =>
        {
            await foreach (var _ in IpcServer.Request(name, new IpcRequest("show")))
                break;
        });
        Assert.Contains("not owned by Winshell", error.Message, StringComparison.Ordinal);
        await waiting;
    }

    [Fact]
    public async Task A_refused_client_is_told_why_and_nothing_runs()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        var loop = new EventQueue();
        var killed = false;
        using var server = new IpcServer(IpcServer.OpenFirst(name), name, loop, () => null!, () => killed = true, _ => "refused for the test");

        var line = await Ask(name, """{"op":"kill"}""");

        var response = System.Text.Json.JsonSerializer.Deserialize(line!, IpcJson.Default.IpcResponse)!;
        Assert.False(response.Ok);
        Assert.Equal("refused for the test", response.Error);
        loop.RunDue();
        Assert.False(killed);
    }

    [Fact]
    public async Task The_name_keeps_an_instance_after_clients_come_and_go()
    {
        var name = "winshell-test-" + Guid.NewGuid().ToString("N");
        using var server = new IpcServer(IpcServer.OpenFirst(name), name, new EventQueue(), () => null!, () => { }, _ => "refused for the test");
        for (var i = 0; i < 20; i++)
        {
            // A refused client and one that hangs up at once both close their instance quickly.
            if (i % 2 == 0)
                await Ask(name, """{"op":"show"}""");
            else
            {
                await using var quick = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
                await quick.ConnectAsync(3000);
            }
            Assert.Throws<IOException>(() => IpcServer.OpenFirst(name).Dispose());
        }
    }

    private static async Task<string?> Ask(string name, string request)
    {
        await using var client = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(3000);
        using var reader = new StreamReader(client, Encoding.UTF8);
        await using var writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
        await writer.WriteLineAsync(request);
        return await reader.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(10));
    }
}
