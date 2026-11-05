using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace Winshell.Updates;

/// <summary>
/// Who signed a file, by its Authenticode signature. A signed Winshell installs only an update whose binaries
/// the same signer, or Microsoft, signed; the digest comes from the same release as the zip, so it cannot tell a forged release apart.
/// </summary>
internal static partial class UpdateSignature
{
    /// <summary>The binaries SignPath signs, as <c>packaging/signpath/artifact-configuration.xml</c> lists them.</summary>
    internal static readonly string[] Signed =
        ["winshell.exe", "winshellw.exe", "winshell.dll", "Winshell.Native.dll", "Winshell.Services.dll", "Winshell.Shell.dll", "Winshell.DefaultShell.dll"];

    /// <summary>Whether <paramref name="subject"/>, from <see cref="Signer"/>, names Microsoft, which signs the .NET runtime and its libraries.</summary>
    internal static bool IsMicrosoft(string? subject) =>
        subject is not null && new X500DistinguishedName(subject).EnumerateRelativeDistinguishedNames()
            .Any(name => name.GetSingleElementType().Value == "2.5.4.10" && name.GetSingleElementValue() == "Microsoft Corporation");

    /// <summary>
    /// The subject of the certificate that signed <paramref name="path"/>, or null when it has no valid embedded
    /// signature. Revocation is not checked and nothing is fetched: this runs before a shell starts, maybe offline.
    /// </summary>
    internal static unsafe string? Signer(string path)
    {
        var action = GenericVerifyV2;
        fixed (char* file = path)
        {
            var fileInfo = new WintrustFileInfo { Size = (uint)sizeof(WintrustFileInfo), FilePath = file };
            var data = new WintrustData
            {
                Size = (uint)sizeof(WintrustData),
                UIChoice = UiNone,
                RevocationChecks = RevokeNone,
                UnionChoice = ChoiceFile,
                File = &fileInfo,
                StateAction = StateActionVerify,
                ProvFlags = CacheOnlyUrlRetrieval,
            };
            try
            {
                if (WinVerifyTrust(-1, &action, &data) != 0)
                    return null;
                var provider = WTHelperProvDataFromStateData(data.StateData);
                var signer = provider == 0 ? 0 : WTHelperGetProvSignerFromChain(provider, 0, 0, 0);
                var cert = signer == 0 ? null : (CryptProviderCert*)WTHelperGetProvCertFromChain(signer, 0);
                if (cert is null || cert->Cert == 0)
                    return null;
                using var certificate = new X509Certificate2(cert->Cert);
                return certificate.Subject;
            }
            finally
            {
                data.StateAction = StateActionClose;
                _ = WinVerifyTrust(-1, &action, &data);
            }
        }
    }

    private static readonly Guid GenericVerifyV2 = new("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");
    private const uint UiNone = 2;
    private const uint RevokeNone = 0;
    private const uint ChoiceFile = 1;
    private const uint StateActionVerify = 1;
    private const uint StateActionClose = 2;
    private const uint CacheOnlyUrlRetrieval = 0x1000;

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WintrustFileInfo
    {
        public uint Size;
        public char* FilePath;
        public nint File;
        public Guid* KnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct WintrustData
    {
        public uint Size;
        public nint PolicyCallbackData;
        public nint SipClientData;
        public uint UIChoice;
        public uint RevocationChecks;
        public uint UnionChoice;
        public WintrustFileInfo* File;
        public uint StateAction;
        public nint StateData;
        public char* UrlReference;
        public uint ProvFlags;
        public uint UIContext;
        public nint SignatureSettings;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CryptProviderCert
    {
        public uint Size;
        public nint Cert;
    }

    [LibraryImport("wintrust.dll")]
    private static unsafe partial int WinVerifyTrust(nint hwnd, Guid* action, WintrustData* data);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperProvDataFromStateData(nint stateData);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperGetProvSignerFromChain(nint provider, uint signer, int counterSigner, uint counterSignerIndex);

    [LibraryImport("wintrust.dll")]
    private static partial nint WTHelperGetProvCertFromChain(nint signer, uint cert);
}
