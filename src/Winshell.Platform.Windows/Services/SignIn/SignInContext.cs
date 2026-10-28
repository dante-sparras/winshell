using System.Runtime.InteropServices;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.SignIn;

public enum SignInResult
{
    Success = 0,
    Failed = 1,
    Error = 2,
    MaxTries = 3,
}

public enum SignInError
{
    StartFailed = 1,
    TryAuthFailed = 2,
    InternalError = 3,
}

/// <summary>
/// The <c>SignInContext</c> on Windows: asks for the account's password and checks it with Windows, as a lock
/// screen needs. There is no PAM, so <c>config</c> and <c>configDirectory</c> are kept but change nothing, and a
/// Windows Hello PIN is not a password.
/// </summary>
[QmlSignal("completed", "result")]
[QmlSignal("error", "error")]
[QmlSignal("pamMessage")]
public sealed partial class SignInContext : QObject
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
            Engine?.Log.Warn("signin", "SignInContext response was ignored as this context does not require one.");
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
                Emit("completed", (int)(ok ? SignInResult.Success : SignInResult.Failed));
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
            Engine?.Log.Warn("signin", $"Cannot set {name} while SignInContext is active");
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

internal static class SignInModule
{
    public static void Register(ModuleRegistry registry) =>
        registry.Module("Winshell")
            .Type<SignInContext>("SignInContext")
            .Enum<SignInResult>(r => r switch
            {
                SignInResult.Success => "Success",
                SignInResult.Failed => "Failed",
                SignInResult.Error => "Error occurred while authenticating",
                SignInResult.MaxTries => "The authentication method has no more attempts available",
                _ => "Invalid result",
            })
            .Enum<SignInError>(e => e switch
            {
                SignInError.StartFailed => "Failed to start the PAM session",
                SignInError.TryAuthFailed => "Failed to try authenticating",
                SignInError.InternalError => "Internal error occurred",
                _ => "Invalid error",
            });
}
