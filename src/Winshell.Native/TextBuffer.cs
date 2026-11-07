using Windows.Win32.Foundation;

namespace Winshell.Native;

/// <summary>
/// Text a Win32 call writes into the caller's buffer. When the buffer is short, the call fails with
/// <c>ERROR_INSUFFICIENT_BUFFER</c> and reports the capacity it needs, so the read grows the buffer once.
/// </summary>
internal static class TextBuffer
{
    /// <summary>
    /// One call. <see cref="Length"/> is the text's length on success, without the terminating null, and the capacity
    /// the call needs on <c>ERROR_INSUFFICIENT_BUFFER</c>.
    /// </summary>
    internal readonly record struct Answer(WIN32_ERROR Code, int Length);

    /// <summary>
    /// The text, or null when <paramref name="query"/> fails for another reason, needs more than
    /// <paramref name="maxCharacters"/>, or is short a second time. A first capacity of 0 passes an empty buffer, which
    /// <c>fixed</c> pins as a null pointer, for a call that reports its size first.
    /// </summary>
    internal static string? Read(int firstCapacity, int maxCharacters, Func<char[], Answer> query)
    {
        var capacity = firstCapacity;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var buffer = new char[capacity];
            var answer = query(buffer);
            if (answer.Code == WIN32_ERROR.ERROR_SUCCESS)
                return (uint)answer.Length > (uint)buffer.Length ? null : new string(buffer, 0, answer.Length);
            if (attempt == 1 || answer.Code != WIN32_ERROR.ERROR_INSUFFICIENT_BUFFER || answer.Length <= capacity ||
                answer.Length > maxCharacters)
                return null;
            capacity = answer.Length;
        }

        return null;
    }
}
