namespace Winshell.Core;

/// <summary>
/// How the shell chooses one look and one Menu when an extension offers them.
/// The first look wins. A shell with none paints <see cref="SurfaceLook.Shell"/>.
/// The Menu is <see cref="SurfaceHands.MenuPluginId"/> when that is set, otherwise the plugin whose kind is <c>menu</c>.
/// A toast is admitted by <see cref="ToastFace.Admit"/>: three on screen, and a toast that stays is not pushed out.
/// </summary>
public static class SurfaceContract
{
    /// <summary>
    /// The look to paint. The first that was offered wins. None paints <see cref="SurfaceLook.Shell"/>.
    /// </summary>
    /// <param name="offered">Looks from extensions, in the order the shell loaded them.</param>
    /// <returns>The look.</returns>
    public static SurfaceLook Look(params SurfaceLook?[] offered)
    {
        if (offered is not null)
        {
            foreach (var look in offered)
            {
                if (look is not null)
                {
                    return look;
                }
            }
        }

        return SurfaceLook.Shell;
    }

    /// <summary>
    /// Which plugin's panel is the Menu. <paramref name="hands"/> wins when it names one.
    /// Otherwise <paramref name="menuKindId"/>, the plugin whose kinds include <c>menu</c>.
    /// </summary>
    /// <param name="hands">The extension's hands, or null.</param>
    /// <param name="menuKindId">A plugin id whose kind is <c>menu</c>, or null when there is none.</param>
    /// <returns>The plugin id, or null when nothing is the Menu.</returns>
    public static string? Menu(SurfaceHands? hands, string? menuKindId)
    {
        var named = hands?.MenuPluginId;
        if (!string.IsNullOrWhiteSpace(named))
        {
            return named.Trim();
        }

        return string.IsNullOrWhiteSpace(menuKindId) ? null : menuKindId.Trim();
    }

    /// <summary>
    /// Whether a new toast is shown among the ones already on screen, newest first.
    /// The same rule as <see cref="ToastFace.Admit"/>.
    /// </summary>
    /// <param name="stays">Whether each toast on screen stays until dismissed, newest first.</param>
    /// <param name="leaves">The index of the toast that leaves, or -1 when none has to.</param>
    /// <returns>False when the new toast is not shown.</returns>
    public static bool Admit(IReadOnlyList<bool> stays, out int leaves) => ToastFace.Admit(stays, out leaves);
}
