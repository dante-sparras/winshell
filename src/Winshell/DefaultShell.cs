using Winshell.Runtime.Shell;

namespace Winshell;

/// <summary>The shell Winshell runs when there is no config: a Windows 11 taskbar on every screen, written out as QML.</summary>
internal static class DefaultShell
{
    /// <summary>Writes the default shell's files to a folder of their own and returns its shell.qml.</summary>
    public static string Install() => Install(ShellLocations.DefaultShellRoot, AnotherInstanceRuns);

    /// <summary>
    /// Writes the shipped files into <paramref name="dir"/>. A running instance of this shell is left alone,
    /// so its watcher does not reload another version's QML. Tests pass the folder and the running check.
    /// </summary>
    internal static string Install(string dir, Func<string, bool> running)
    {
        Directory.CreateDirectory(dir);
        var shell = Path.Combine(dir, "shell.qml");
        // An older instance is still drawing this folder. Rewriting it would hot-reload the other version's QML.
        if (running(shell))
            return shell;

        var assembly = typeof(DefaultShell).Assembly;
        var shipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in assembly.GetManifestResourceNames())
        {
            if (!name.StartsWith("DefaultShell/", StringComparison.Ordinal))
                continue;
            var relative = name["DefaultShell/".Length..].Replace('/', Path.DirectorySeparatorChar);
            shipped.Add(relative);
            var target = Path.Combine(dir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            var text = reader.ReadToEnd();
            // Keep the files as they ship, so a newer Winshell brings its newer shell. Another instance starting at
            // the same time may be writing the same file; then it is left to that one.
            try
            {
                if (!File.Exists(target) || File.ReadAllText(target) != text)
                    File.WriteAllText(target, text);
            }
            catch (IOException)
            {
            }
        }
        RemoveDropped(dir, dir, shipped);
        return shell;
    }

    /// <summary>True when a running instance's config is this shell file.</summary>
    private static bool AnotherInstanceRuns(string shell)
    {
        if (!TryFullPath(shell, out var full))
            return false;
        foreach (var (info, _) in Instances.List(false))
        {
            if (TryFullPath(info.ConfigPath, out var config) && string.Equals(config, full, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TryFullPath(string path, out string full)
    {
        try
        {
            full = Path.GetFullPath(path);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or IOException or NotSupportedException)
        {
            full = "";
            return false;
        }
    }

    /// <summary>
    /// Deletes .qml and .js files this build does not ship. User files such as launcher.toml stay:
    /// the default shell folder is rewritten, and a copy the user edits lives somewhere else.
    /// </summary>
    private static void RemoveDropped(string root, string dir, HashSet<string> shipped)
    {
        foreach (var file in Directory.EnumerateFiles(dir))
        {
            var ext = Path.GetExtension(file);
            if (ext is not (".qml" or ".js"))
                continue;
            var relative = Path.GetRelativePath(root, file);
            if (shipped.Contains(relative))
                continue;
            try
            {
                File.Delete(file);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }
        foreach (var sub in Directory.EnumerateDirectories(dir))
            RemoveDropped(root, sub, shipped);
    }
}
