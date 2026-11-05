using System.Security.Cryptography.X509Certificates;
using Winshell.Native;

namespace Winshell.Updates;

/// <summary>
/// Who signed a file, by its Authenticode signature. A signed Winshell installs only an update whose binaries
/// the same signer, or Microsoft, signed; the digest comes from the same release as the zip, so it cannot tell a forged release apart.
/// </summary>
internal static class UpdateSignature
{
    /// <summary>The binaries SignPath signs, as <c>packaging/signpath/artifact-configuration.xml</c> lists them.</summary>
    internal static readonly string[] Signed =
        ["winshell.exe", "winshellw.exe", "winshell.dll", "Winshell.Native.dll", "Winshell.Core.dll", "Winshell.Services.dll", "Winshell.Shell.dll"];

    /// <summary>Whether <paramref name="subject"/>, from <see cref="Authenticode.Signer"/>, names Microsoft, which signs the .NET runtime and its libraries.</summary>
    internal static bool IsMicrosoft(string? subject) =>
        subject is not null && new X500DistinguishedName(subject).EnumerateRelativeDistinguishedNames()
            .Any(name => name.GetSingleElementType().Value == "2.5.4.10" && name.GetSingleElementValue() == "Microsoft Corporation");
}
