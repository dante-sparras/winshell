using System.Xml.Linq;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Services.Notifications;

/// <summary>A native toast sent under an installed Windows AppUserModelID.</summary>
[QmlSignal("failed", "message")]
public sealed class ToastMessage : QObject
{
    private readonly Action<string, string, string, string, Action<string>> _send;
    private string _appId = "";
    private string _title = "";
    private string _body = "";
    private string _activationUri = "";
    private string _tag = "";
    private string _group = "";
    private bool _silent;
    private string _error = "";

    public ToastMessage() : this(SendNative) { }
    internal ToastMessage(Action<string, string, string, string, Action<string>> send)
    {
        _send = send;
        Actions = new QmlList<ToastAction>(this, "actions", added: action => action.SetOwner(this));
    }

    [Qml] public string AppId { get { Track(); return _appId; } set => SetField(ref _appId, value ?? ""); }
    [Qml] public string Title { get { Track(); return _title; } set => SetField(ref _title, value ?? ""); }
    [Qml] public string Body { get { Track(); return _body; } set => SetField(ref _body, value ?? ""); }
    [Qml] public string ActivationUri { get { Track(); return _activationUri; } set => SetField(ref _activationUri, value ?? ""); }
    [Qml] public string Tag { get { Track(); return _tag; } set => SetField(ref _tag, value ?? ""); }
    [Qml] public string Group { get { Track(); return _group; } set => SetField(ref _group, value ?? ""); }
    [Qml] public bool Silent { get { Track(); return _silent; } set => SetField(ref _silent, value); }
    [Qml] public string Error { get { Track(); return _error; } }
    [Qml] public QmlList<ToastAction> Actions { get; }

    [Qml] public bool Send()
    {
        if (IsDestroyed)
            return false;
        try
        {
            if (string.IsNullOrWhiteSpace(AppId))
                throw new ArgumentException("toast.appId must be an installed Windows AppUserModelID.");
            if (string.IsNullOrWhiteSpace(Title))
                throw new ArgumentException("toast.title must not be empty.");
            if (Tag.Length > 64 || Group.Length > 64)
                throw new ArgumentException("Toast tag and group must be at most 64 characters.");
            if (Group.Length > 0 && Tag.Length == 0)
                throw new ArgumentException("A toast group requires a tag.");
            SetField(ref _error, "", nameof(Error));
            _send(AppId, BuildXml(), Tag, Group, ReportFailure);
            return true;
        }
        catch (Exception e) when (WindowsNotificationBackend.IsExpectedFailure(e))
        {
            ReportFailure(e.Message);
            return false;
        }
    }

    internal string BuildXml()
    {
        var root = new XElement("toast",
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", Title), new XElement("text", Body))));
        if (ActivationUri.Length > 0)
        {
            ValidateUri(ActivationUri);
            root.Add(new XAttribute("activationType", "protocol"), new XAttribute("launch", ActivationUri));
        }
        if (Actions.Count > 5)
            throw new ArgumentException("Windows toasts support at most five actions.");
        if (Actions.Count > 0)
        {
            var actions = new XElement("actions");
            foreach (var action in Actions.Cast<ToastAction>())
            {
                if (string.IsNullOrWhiteSpace(action.Text))
                    throw new ArgumentException("A toast action needs text.");
                ValidateUri(action.ActivationUri);
                actions.Add(new XElement("action", new XAttribute("content", action.Text),
                    new XAttribute("activationType", "protocol"), new XAttribute("arguments", action.ActivationUri)));
            }
            root.Add(actions);
        }
        if (Silent)
            root.Add(new XElement("audio", new XAttribute("silent", "true")));
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private static void ValidateUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out _))
            throw new ArgumentException("Toast activation requires an absolute protocol URI.");
    }

    private void ReportFailure(string message)
    {
        void Report()
        {
            if (IsDestroyed) return;
            SetField(ref _error, string.IsNullOrWhiteSpace(message) ? "Windows could not send the toast." : message, nameof(Error));
            Emit("failed", _error);
        }
        if (Engine?.Loop is { } loop) loop.Post(Report);
        else Report();
    }

    private static void SendNative(string appId, string xml, string tag, string group, Action<string> failed)
    {
        var content = new XmlDocument();
        content.LoadXml(xml);
        var toast = new ToastNotification(content);
        toast.Failed += (_, args) => failed($"Windows rejected the toast: {args.ErrorCode.Message}");
        if (tag.Length > 0) toast.Tag = tag;
        if (group.Length > 0) toast.Group = group;
        var notifier = ToastNotificationManager.CreateToastNotifier(appId);
        if (notifier.Setting != NotificationSetting.Enabled)
            throw new InvalidOperationException($"Windows disabled toast notifications for '{appId}': {notifier.Setting}.");
        notifier.Show(toast);
    }
}

public sealed class ToastAction : QObject
{
    private string _text = "";
    private string _activationUri = "";
    [Qml] public string Text { get { Track(); return _text; } set => SetField(ref _text, value ?? ""); }
    [Qml] public string ActivationUri { get { Track(); return _activationUri; } set => SetField(ref _activationUri, value ?? ""); }
}
