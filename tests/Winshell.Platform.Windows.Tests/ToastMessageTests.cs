using System.Runtime.InteropServices;
using System.Xml.Linq;
using Winshell.Platform.Windows.Services.Notifications;
using Winshell.Runtime;

namespace Winshell.Platform.Windows.Tests;

public class ToastMessageTests
{
    [Fact]
    public void Toast_payload_escapes_text_and_encodes_native_protocol_actions()
    {
        string? sentApp = null;
        string? sentXml = null;
        string? sentTag = null;
        string? sentGroup = null;
        var toast = new ToastMessage((app, xml, tag, group, _) =>
        {
            sentApp = app; sentXml = xml; sentTag = tag; sentGroup = group;
        })
        {
            AppId = "Winshell.Test", Title = "<hello> & world", Body = "First\nSecond & third",
            ActivationUri = "https://example.com/?a=1&b=2", Tag = "mail", Group = "inbox", Silent = true,
        };
        toast.Actions.Add(new ToastAction { Text = "Read & reply", ActivationUri = "mailto:test@example.com" });
        Assert.True(toast.Send());
        Assert.Equal("Winshell.Test", sentApp);
        Assert.Equal("mail", sentTag);
        Assert.Equal("inbox", sentGroup);
        var xml = XElement.Parse(sentXml!);
        Assert.Equal("protocol", (string?)xml.Attribute("activationType"));
        Assert.Equal(toast.ActivationUri, (string?)xml.Attribute("launch"));
        Assert.Equal(new[] { toast.Title, toast.Body }, xml.Descendants("text").Select(e => e.Value));
        var action = Assert.Single(xml.Descendants("action"));
        Assert.Equal("protocol", (string?)action.Attribute("activationType"));
        Assert.Equal("Read & reply", (string?)action.Attribute("content"));
        Assert.Equal("mailto:test@example.com", (string?)action.Attribute("arguments"));
        Assert.Equal("true", (string?)xml.Element("audio")?.Attribute("silent"));
        toast.Destroy();
        Assert.True(toast.Actions[0].IsDestroyed);
    }

    [Theory]
    [InlineData("", "Title", "")]
    [InlineData("App", " ", "")]
    [InlineData("App", "Title", "relative/path")]
    public void Invalid_input_is_reported_without_sending(string appId, string title, string uri)
    {
        var calls = 0;
        var toast = new ToastMessage((_, _, _, _, _) => calls++) { AppId = appId, Title = title, ActivationUri = uri };
        string? failure = null;
        toast.Connect("failed", args => failure = (string)args[0]!);
        Assert.False(toast.Send());
        Assert.Equal(0, calls);
        Assert.False(string.IsNullOrEmpty(failure));
        Assert.Equal(failure, toast.Error);
    }

    [Fact]
    public void Invalid_action_and_tag_group_limits_are_rejected_before_sending()
    {
        var calls = 0;
        var toast = new ToastMessage((_, _, _, _, _) => calls++) { AppId = "App", Title = "Title" };
        toast.Actions.Add(new ToastAction { Text = "Open", ActivationUri = "relative" });
        Assert.False(toast.Send());
        toast.Actions[0].ActivationUri = "https://example.com";
        toast.Actions[0].Text = "";
        Assert.False(toast.Send());
        toast.Actions.Clear();
        for (var i = 0; i < 6; i++)
            toast.Actions.Add(new ToastAction { Text = "Open", ActivationUri = "https://example.com" });
        Assert.False(toast.Send());
        toast.Actions.Clear();
        toast.Tag = new string('a', 65);
        Assert.False(toast.Send());
        toast.Tag = "tag";
        toast.Group = new string('a', 65);
        Assert.False(toast.Send());
        toast.Tag = "";
        toast.Group = "group";
        Assert.False(toast.Send());
        Assert.Equal(0, calls);
    }

    [Fact]
    public void Native_send_failure_is_reported_and_a_later_send_clears_the_error()
    {
        var fail = true;
        var toast = new ToastMessage((_, _, _, _, _) => { if (fail) throw new COMException("No registered identity"); })
        { AppId = "App", Title = "Title" };
        Assert.False(toast.Send());
        Assert.Equal("No registered identity", toast.Error);
        fail = false;
        Assert.True(toast.Send());
        Assert.Equal("", toast.Error);
        toast.Destroy();
        Assert.False(toast.Send());
    }

    [Fact]
    public void Asynchronous_native_failure_is_posted_to_the_engine_loop_and_ignored_after_destruction()
    {
        Action<string>? failure = null;
        var toast = new ToastMessage((_, _, _, _, callback) => failure = callback) { AppId = "App", Title = "Title" };
        var loop = new ManualEventLoop();
        using var engine = new QmlEngine(StandardModules.Create(), loop);
        toast.Engine = engine;
        var signals = 0;
        toast.Connect("failed", _ => signals++);
        Assert.True(toast.Send());
        failure!("Rejected");
        Assert.Equal("", toast.Error);
        loop.Process();
        Assert.Equal("Rejected", toast.Error);
        Assert.Equal(1, signals);
        failure("Too late");
        toast.Destroy();
        loop.Process();
        Assert.Equal(1, signals);
    }

    [Fact]
    public void Qml_loads_toast_actions_and_calls_send_without_a_live_notification()
    {
        var directory = Path.Combine(Path.GetTempPath(), "winshell-notifications", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "shell.qml");
            File.WriteAllText(path, """
                import QtQml
                import Winshell.Notifications
                ToastMessage {
                    appId: ""
                    title: "Hello"
                    body: "From QML"
                    actions: [ToastAction { text: "Open"; activationUri: "https://example.com" }]
                    property bool accepted: false
                    Component.onCompleted: accepted = send()
                }
                """);
            var registry = StandardModules.Create();
            NotificationModule.Register(registry);
            var loop = new ManualEventLoop();
            using var engine = new QmlEngine(registry, loop, new Log(), directory);
            var toast = Assert.IsType<ToastMessage>(engine.CreateRoot(path));
            loop.Process();
            Assert.False((bool)toast.ReadProperty("accepted")!);
            Assert.Contains("installed Windows AppUserModelID", toast.Error);
            Assert.Equal("Open", (string?)Assert.Single(XElement.Parse(toast.BuildXml()).Descendants("action")).Attribute("content"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
