using System.Collections.ObjectModel;
using System.Xml.Linq;

namespace Winshell.Notifications;

/// <summary>A native toast sent under an installed Windows AppUserModelID.</summary>
public sealed class ToastMessage : ObservableObject
{
    private readonly IEventLoop _loop;
    private readonly INotificationBackend _backend;
    private string _appId = "";
    private string _title = "";
    private string _body = "";
    private string _activationUri = "";
    private string _tag = "";
    private string _group = "";
    private bool _silent;
    private string _error = "";

    /// <summary>Creates an empty toast whose <see cref="Failed"/> event is raised on the given event loop.</summary>
    public ToastMessage(IEventLoop loop)
        : this(loop, new WindowsNotificationBackend())
    {
    }

    internal ToastMessage(IEventLoop loop, INotificationBackend backend)
    {
        _loop = loop;
        _backend = backend;
    }

    /// <summary>A send failed, at once or when Windows later rejected the toast. The text is <see cref="Error"/>.</summary>
    public event Action<string>? Failed;

    /// <summary>The AppUserModelID of the sending app's installed Windows identity. Required.</summary>
    public string AppId
    {
        get => _appId;
        set => Set(ref _appId, value ?? "");
    }

    /// <summary>The first line. Required.</summary>
    public string Title
    {
        get => _title;
        set => Set(ref _title, value ?? "");
    }

    /// <summary>The text under the title, as plain text.</summary>
    public string Body
    {
        get => _body;
        set => Set(ref _body, value ?? "");
    }

    /// <summary>An absolute protocol URI Windows opens when the user clicks the toast. Without one, the toast is informational.</summary>
    public string ActivationUri
    {
        get => _activationUri;
        set => Set(ref _activationUri, value ?? "");
    }

    /// <summary>An optional Windows replacement identifier, at most 64 characters.</summary>
    public string Tag
    {
        get => _tag;
        set => Set(ref _tag, value ?? "");
    }

    /// <summary>An optional Windows replacement group, at most 64 characters. Needs a <see cref="Tag"/>.</summary>
    public string Group
    {
        get => _group;
        set => Set(ref _group, value ?? "");
    }

    /// <summary>Whether the toast plays no sound.</summary>
    public bool Silent
    {
        get => _silent;
        set => Set(ref _silent, value);
    }

    /// <summary>Why the last send failed. Cleared by a send that passes validation.</summary>
    public string Error => _error;

    /// <summary>Up to five buttons on the toast.</summary>
    public ObservableCollection<ToastAction> Actions { get; } = [];

    /// <summary>Sends the toast. Returns whether validation and submission succeeded; Windows can still hold back the banner.</summary>
    public bool Send()
    {
        if (Invalid() is { } problem)
        {
            ReportFailure(problem);
            return false;
        }

        Set(ref _error, "", nameof(Error));
        try
        {
            _backend.Show(AppId, BuildXml(), Tag, Group, ReportFailure);
            return true;
        }
        catch (Exception e) when (WindowsFailure.IsExpected(e))
        {
            ReportFailure(e.Message);
            return false;
        }
    }

    private string? Invalid()
    {
        if (string.IsNullOrWhiteSpace(AppId))
            return "toast.appId must be an installed Windows AppUserModelID.";
        if (string.IsNullOrWhiteSpace(Title))
            return "toast.title must not be empty.";
        if (Tag.Length > 64 || Group.Length > 64)
            return "Toast tag and group must be at most 64 characters.";
        if (Group.Length > 0 && Tag.Length == 0)
            return "A toast group requires a tag.";
        if (Actions.Count > 5)
            return "Windows toasts support at most five actions.";
        if (Actions.Any(a => string.IsNullOrWhiteSpace(a.Text)))
            return "A toast action needs text.";
        if ((ActivationUri.Length > 0 && !IsAbsolute(ActivationUri)) || Actions.Any(a => !IsAbsolute(a.ActivationUri)))
            return "Toast activation requires an absolute protocol URI.";
        return null;
    }

    private static bool IsAbsolute(string uri)
    {
        return Uri.TryCreate(uri, UriKind.Absolute, out _);
    }

    private string BuildXml()
    {
        var root = new XElement("toast",
            new XElement("visual", new XElement("binding", new XAttribute("template", "ToastGeneric"),
                new XElement("text", Title), new XElement("text", Body))));
        if (ActivationUri.Length > 0)
            root.Add(new XAttribute("activationType", "protocol"), new XAttribute("launch", ActivationUri));
        if (Actions.Count > 0)
            root.Add(new XElement("actions", Actions.Select(a => new XElement("action",
                new XAttribute("content", a.Text), new XAttribute("activationType", "protocol"),
                new XAttribute("arguments", a.ActivationUri)))));
        if (Silent)
            root.Add(new XElement("audio", new XAttribute("silent", "true")));
        return root.ToString(SaveOptions.DisableFormatting);
    }

    private void ReportFailure(string message)
    {
        _loop.Post(() =>
        {
            Set(ref _error, string.IsNullOrWhiteSpace(message) ? "Windows could not send the toast." : message,
                nameof(Error));
            Failed?.Invoke(_error);
        });
    }
}

/// <summary>A button on a toast.</summary>
public sealed class ToastAction : ObservableObject
{
    private string _text = "";
    private string _activationUri = "";

    /// <summary>The button's label. Required.</summary>
    public string Text
    {
        get => _text;
        set => Set(ref _text, value ?? "");
    }

    /// <summary>The absolute protocol URI Windows opens when the user clicks the button.</summary>
    public string ActivationUri
    {
        get => _activationUri;
        set => Set(ref _activationUri, value ?? "");
    }
}
