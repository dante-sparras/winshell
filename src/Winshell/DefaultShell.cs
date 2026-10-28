namespace Winshell;

/// <summary>The shell Winshell runs when there is no config: a Windows 11 style bar, written out as QML.</summary>
internal static class DefaultShell
{
    /// <summary>Writes the default shell's files to a folder of their own and returns its shell.qml.</summary>
    public static string Install()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "winshell", "default-shell");
        Directory.CreateDirectory(dir);
        var assembly = typeof(DefaultShell).Assembly;
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("DefaultShell/", StringComparison.Ordinal))
                continue;
            var relative = name["DefaultShell/".Length..].Replace('/', Path.DirectorySeparatorChar);
            var target = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            // Keep the files as they ship, so a newer Winshell brings its newer shell.
            if (!File.Exists(target) || File.ReadAllText(target) != text)
                File.WriteAllText(target, text);
        }
        return Path.Combine(dir, "shell.qml");
    }
}
