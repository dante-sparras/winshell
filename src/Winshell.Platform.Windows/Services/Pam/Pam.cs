using System.Runtime.InteropServices;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Pam;

public enum PamResult
{
    Success = 0,
    Failed = 1,
    Error = 2,
    MaxTries = 3,
}

public enum PamError
{
    StartFailed = 1,
    TryAuthFailed = 2,
    InternalError = 3,
}

/// <summary>
/// Quickshell's <c>PamContext</c> on Windows: asks for the account's password and checks it with Windows, as a lock
/// screen needs. There is no PAM, so <c>config</c> and <c>configDirectory</c> are kept but change nothing, and a
/// Windows Hello PIN is not a password.
/// </summary>
[QmlSignal("completed", "result")]
[QmlSignal("error", "error")]
[QmlSignal("pamMessage")]
public sealed partial class PamContext : QObject
{
    private string _user = "";
    private string _config = "login";
    private string _configDirectory = "/etc/pam.d";
    private bool _active;
    private string _message = "";
    private bool _messageIsError;
    private bool _responseRequired;
    private int _conversation;

    [Qml]
    public string User
    {
        get { Track(); return _user; }
        set => SetWhileInactive(ref _user, value ?? "", "user");
    }

    [Qml]
    public string Config
    {
        get { Track(); return _config; }
        set => SetWhileInactive(ref _config, value ?? "", "config");
    }

    [Qml]
    public string ConfigDirectory
    {
        get { Track(); return _configDirectory; }
        set => SetWhileInactive(ref _configDirectory, value ?? "", "configDirectory");
    }

    [Qml]
    public bool Active
    {
        get { Track(); return _active; }
        set
        {
            if (value)
                Start();
            else
                Abort();
        }
    }

    [Qml] public string Message { get { Track(); return _message; } }

    [Qml] public bool MessageIsError { get { Track(); return _messageIsError; } }

    [Qml] public bool ResponseRequired { get { Track(); return _responseRequired; } }

    [Qml] public bool ResponseVisible { get { Track(); return false; } }

    [Qml]
    public bool Start()
    {
        if (_active)
            return true;
        _conversation++;
        SetField(ref _active, true, "active");
        Prompt("Password: ", isError: false);
        return true;
    }

    [Qml]
    public void Abort()
    {
        if (!_active)
            return;
        _conversation++;
        SetField(ref _active, false, "active");
        SetField(ref _message, "", "message");
        SetField(ref _messageIsError, false, "messageIsError");
        SetField(ref _responseRequired, false, "responseRequired");
    }

    [Qml]
    public void Respond(string response)
    {
        if (!_active || !_responseRequired)
        {
            Engine?.Log.Warn("quickshell.service.pam", "PamContext response was ignored as this context does not require one.");
            return;
        }
        SetField(ref _responseRequired, false, "responseRequired");
        var conversation = _conversation;
        var user = _user.Length > 0 ? _user : Environment.UserName;
        var domain = _user.Contains('\\') ? null : Environment.UserDomainName;
        var loop = Engine?.Loop;
        // Windows takes a moment to check, and longer to refuse: keep it off the UI thread.
        Task.Run(() => Check(user, domain, response)).ContinueWith(t =>
        {
            var ok = t.IsCompletedSuccessfully && t.Result;
            loop?.Post(() =>
            {
                if (conversation != _conversation)
                    return;
                Abort();
                Emit("completed", (int)(ok ? PamResult.Success : PamResult.Failed));
            });
        }, TaskScheduler.Default);
    }

    private void Prompt(string message, bool isError)
    {
        SetField(ref _message, message, "message");
        SetField(ref _messageIsError, isError, "messageIsError");
        SetField(ref _responseRequired, true, "responseRequired");
        Emit("pamMessage");
    }

    private void SetWhileInactive(ref string field, string value, string name)
    {
        if (_active)
        {
            Engine?.Log.Warn("quickshell.service.pam", $"Cannot set {name} while PamContext is active");
            return;
        }
        SetField(ref field, value, name);
    }

    private static bool Check(string user, string? domain, string password)
    {
        if (!LogonUserW(user, domain, password, LOGON32_LOGON_INTERACTIVE, LOGON32_PROVIDER_DEFAULT, out var token))
            return false;
        CloseHandle(token);
        return true;
    }

    protected override void OnDestroy() => _conversation++;

    private const int LOGON32_LOGON_INTERACTIVE = 2;
    private const int LOGON32_PROVIDER_DEFAULT = 0;

    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool LogonUserW(string user, string? domain, string password, int logonType, int provider, out nint token);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}

internal static class PamModule
{
    public static void Register(ModuleRegistry registry) =>
        registry.Module("Quickshell.Services.Pam")
            .Type<PamContext>("PamContext")
            .Enum<PamResult>(r => r switch
            {
                PamResult.Success => "Success",
                PamResult.Failed => "Failed",
                PamResult.Error => "Error occurred while authenticating",
                PamResult.MaxTries => "The authentication method has no more attempts available",
                _ => "Invalid result",
            })
            .Enum<PamError>(e => e switch
            {
                PamError.StartFailed => "Failed to start the PAM session",
                PamError.TryAuthFailed => "Failed to try authenticating",
                PamError.InternalError => "Internal error occurred",
                _ => "Invalid error",
            });
}
