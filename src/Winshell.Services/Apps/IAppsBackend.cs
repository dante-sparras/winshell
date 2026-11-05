namespace Winshell.Apps;

/// <summary>The Start menu's apps and their usage as Windows records them, and how Windows opens an app.</summary>
internal interface IAppsBackend
{
    /// <summary>Raised on any thread after a scan changed <see cref="Snapshot"/>.</summary>
    event Action? Changed;

    /// <summary>The apps and the usage from the latest scan, the apps combined and in name order. Empty before the first.</summary>
    AppSnapshot Snapshot { get; }

    /// <summary>Starts scanning and following changes. Does nothing when it already has.</summary>
    void Start();

    /// <summary>Opens a shortcut through the shell, in <paramref name="startIn"/> when that folder exists. Null when it opened.</summary>
    string? OpenShortcut(string path, string startIn);

    /// <summary>
    /// Activates a packaged app and returns at once. The task completes once the app is up, seconds on a cold start,
    /// with null or the error. It does not fault.
    /// </summary>
    Task<string?> ActivateAsync(string appUserModelId, string arguments);

    /// <summary>Opens a packaged app through <c>shell:AppsFolder</c>. Null when it opened.</summary>
    string? OpenAppsFolder(string appUserModelId);
}
