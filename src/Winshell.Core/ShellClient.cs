using System.IO.Pipes;
using System.Text;

namespace Winshell.Core;

/// <summary>Sends one line to the shell and prints the reply. The process then leaves.</summary>
public static class ShellClient
{
    /// <summary>
    /// Sends <paramref name="line"/> to <paramref name="pipeName"/> and prints the reply.
    /// The shell closes the pipe after it writes. Closing this end can throw, and that is not a missed shell.
    /// </summary>
    /// <param name="pipeName">The shell's pipe for this Windows session.</param>
    /// <param name="line">The request, without a newline.</param>
    /// <param name="output">Where a reply is printed.</param>
    /// <param name="error">Where a refusal is printed.</param>
    /// <returns>0 when the shell answered. 1 when the line was refused or the shell is absent.</returns>
    public static int Ask(string pipeName, string line, TextWriter output, TextWriter error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);
        if (!ShellWords.TryParse(line, out _, out var refused))
        {
            error.WriteLine(refused);
            return 1;
        }

        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.CurrentUserOnly);
        try
        {
            pipe.Connect(ShellPipe.TimeoutMilliseconds);
            var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), bufferSize: 1024, leaveOpen: true)
            {
                AutoFlush = true,
            };
            var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 16 * 1024, leaveOpen: true);
            try
            {
                writer.WriteLine(line);
                var reply = reader.ReadToEnd().TrimEnd('\r', '\n');
                if (reply.Length == 0)
                {
                    error.WriteLine("Winshell is not running");
                    return 1;
                }

                if (Refused(reply))
                {
                    error.WriteLine(reply);
                    return 1;
                }

                output.WriteLine(reply);
                return 0;
            }
            finally
            {
                Close(reader);
                Close(writer);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or IOException)
        {
            error.WriteLine("Winshell is not running");
            return 1;
        }
        finally
        {
            Close(pipe);
        }
    }

    private static void Close(IDisposable item)
    {
        try
        {
            item.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException)
        {
        }
    }

    private static bool Refused(string reply) =>
        reply.StartsWith("Usage:", StringComparison.Ordinal)
        || reply.StartsWith("Unknown command ", StringComparison.Ordinal)
        || reply is "the shell did not answer" or "the shell is stopping" or "Winshell is not running";
}
