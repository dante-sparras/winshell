using System.Security.Cryptography.X509Certificates;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security.WinTrust;

namespace Winshell.Native;

/// <summary>A file's embedded Authenticode signature, checked by <c>WinVerifyTrust</c> without going online.</summary>
internal static unsafe class Authenticode
{
    /// <summary>
    /// The subject of the certificate that signed <paramref name="path"/>, or null when it has no valid embedded
    /// signature. Revocation is not checked and nothing is fetched.
    /// </summary>
    internal static string? Signer(string path)
    {
        var action = PInvoke.WINTRUST_ACTION_GENERIC_VERIFY_V2;
        fixed (char* file = path)
        {
            var fileInfo = new WINTRUST_FILE_INFO { cbStruct = (uint)sizeof(WINTRUST_FILE_INFO), pcwszFilePath = file };
            var data = new WINTRUST_DATA
            {
                cbStruct = (uint)sizeof(WINTRUST_DATA),
                dwUIChoice = WINTRUST_DATA_UICHOICE.WTD_UI_NONE,
                fdwRevocationChecks = WINTRUST_DATA_REVOCATION_CHECKS.WTD_REVOKE_NONE,
                dwUnionChoice = WINTRUST_DATA_UNION_CHOICE.WTD_CHOICE_FILE,
                dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_VERIFY,
                dwProvFlags = WINTRUST_DATA_PROVIDER_FLAGS.WTD_CACHE_ONLY_URL_RETRIEVAL,
            };
            data.pFile = &fileInfo;
            var noWindow = new HWND(-1);
            try
            {
                if (PInvoke.WinVerifyTrust(noWindow, &action, &data) != 0)
                    return null;
                var provider = PInvoke.WTHelperProvDataFromStateData(data.hWVTStateData);
                var signer = provider == null ? null : PInvoke.WTHelperGetProvSignerFromChain(provider, 0, false, 0);
                var cert = signer == null ? null : PInvoke.WTHelperGetProvCertFromChain(signer, 0);
                if (cert == null || cert->pCert == null)
                    return null;
                using var certificate = new X509Certificate2((nint)cert->pCert);
                return certificate.Subject;
            }
            finally
            {
                data.dwStateAction = WINTRUST_DATA_STATE_ACTION.WTD_STATEACTION_CLOSE;
                _ = PInvoke.WinVerifyTrust(noWindow, &action, &data);
            }
        }
    }
}
