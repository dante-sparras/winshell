using Winshell.Native;

namespace Winshell.Platform.Windows.Tests;

public class ProcessImageTests
{
    [Fact]
    public void A_path_longer_than_the_first_buffer_is_read_on_the_retry()
    {
        var path = new string('a', 2000);
        var calls = 0;
        var read = ProcessImage.Read((buffer, _) =>
        {
            calls++;
            if (buffer.Length < path.Length)
                return new ProcessImage.QueryResult(false, path.Length + 1, ProcessImage.InsufficientBuffer);
            path.CopyTo(buffer);
            return new ProcessImage.QueryResult(true, path.Length, 0);
        });

        Assert.Equal(2, calls);
        Assert.Equal(path, read);
    }

    [Fact]
    public void A_query_that_fails_for_another_reason_returns_nothing()
    {
        Assert.Null(ProcessImage.Read((_, _) => new ProcessImage.QueryResult(false, 0, 5)));
    }

    [Fact]
    public void A_required_size_past_the_win32_limit_returns_nothing()
    {
        Assert.Null(ProcessImage.Read((_, _) => new ProcessImage.QueryResult(false, ProcessImage.MaxCharacters + 1, ProcessImage.InsufficientBuffer)));
    }

    [Fact]
    public void A_second_short_buffer_returns_nothing()
    {
        Assert.Null(ProcessImage.Read((_, capacity) => new ProcessImage.QueryResult(false, capacity + 1, ProcessImage.InsufficientBuffer)));
    }

    [Fact]
    public void This_process_image_is_the_running_program()
    {
        var path = ProcessImage.TryRead((uint)Environment.ProcessId);
        Assert.Equal(Environment.ProcessPath, path, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void An_unknown_process_has_no_image()
    {
        Assert.Null(ProcessImage.TryRead(0));
    }
}
