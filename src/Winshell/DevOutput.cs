using System.Text.RegularExpressions;

namespace Winshell;

/// <summary>
/// Reads what <c>dotnet watch</c> prints. <see cref="Read"/> gives the text of the notice a line calls for: the compile
/// errors so far, or empty once the code is applied. Null means the line changes nothing on screen.
/// </summary>
/// <param name="projectFolder">The project's folder. An error in a file under it shows the file's path from there.</param>
internal sealed partial class DevOutput(string projectFolder)
{
    private const string Prefix = "dotnet watch : ";
    private readonly List<string> _errors = [];

    public string? Read(string line)
    {
        var text = line.StartsWith(Prefix, StringComparison.Ordinal) ? line[Prefix.Length..] : line.Trim();
        if (ConfigurationProject.IsErrorLine(text))
        {
            var error = ConfigurationProject.ErrorLine(text, projectFolder);
            if (_errors.Contains(error) || _errors.Count == ConfigurationProject.MostErrors)
                return null;
            _errors.Add(error);
            return string.Join('\n', _errors);
        }

        if (text.StartsWith("Unable to apply changes", StringComparison.Ordinal))
            _errors.Clear();
        if (!Applied().IsMatch(text))
            return null;
        _errors.Clear();
        return "";
    }

    [GeneratedRegex(@"changes applied in \d+ms|^Build succeeded")]
    private static partial Regex Applied();
}
