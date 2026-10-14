namespace Winshell.Core;

/// <summary>
/// Writes a person's Winshell file that includes the copy Winoma shipped, when the person has no file yet.
/// Winshell alone, with no shipped copy beside the program, starts from the built-in defaults and writes nothing here.
/// The built-in default leaves the Windows taskbar shown. The copy Winoma ships sets it hidden.
/// </summary>
public static class WinshellFiles
{
    /// <summary>
    /// Ensures <c>settings.json</c> exists in <see cref="ConfigFolder"/> when this program has a shipped copy.
    /// A file the person already has is left as it is.
    /// </summary>
    /// <param name="baseDirectory">The program's folder, <see cref="AppContext.BaseDirectory"/>.</param>
    public static void Ensure(string baseDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);
        var shipped = Path.Combine(baseDirectory, "defaults", "winshell", ConfigFolder.SettingsFile);
        var person = Path.Combine(ConfigFolder.Path, ConfigFolder.SettingsFile);
        if (!File.Exists(shipped) || File.Exists(person))
        {
            return;
        }

        Directory.CreateDirectory(ConfigFolder.Path);
        File.WriteAllText(person, "{\n  \"include\": [\n    " + Quote(Path.GetFullPath(shipped)) + "\n  ]\n}\n");
    }

    private static string Quote(string text)
    {
        var buffer = new System.Text.StringBuilder(text.Length + 2);
        buffer.Append('"');
        foreach (var character in text)
        {
            switch (character)
            {
                case '\\':
                    buffer.Append("\\\\");
                    break;
                case '"':
                    buffer.Append("\\\"");
                    break;
                default:
                    buffer.Append(character);
                    break;
            }
        }

        buffer.Append('"');
        return buffer.ToString();
    }
}
