namespace Winshell.Runtime.Tests;

/// <summary>A shell folder in a temporary directory, with an engine on a manual event loop.</summary>
public sealed class TestShell : IDisposable
{
    private QmlEngine? _engine;

    public TestShell(params (string Name, string Text)[] files)
    {
        Directory = Path.Combine(Path.GetTempPath(), "winshell-tests", Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(Directory);
        foreach (var (name, text) in files)
            Write(name, text);
        Log = new Log { Sink = (level, category, message) => Messages.Add($"{level} {category}: {message}") };
    }

    public string Directory { get; }

    public ManualEventLoop Loop { get; } = new();

    public Log Log { get; }

    public List<string> Messages { get; } = [];

    public QmlEngine Engine => _engine ??= new QmlEngine(StandardModules.Create(), Loop, Log, Directory);

    public void Write(string name, string text)
    {
        var path = Path.Combine(Directory, name);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, text);
    }

    public QObject Load(string name = "shell.qml")
    {
        var root = Engine.CreateRoot(Path.Combine(Directory, name));
        Loop.Process();
        return root;
    }

    public static QObject Run(string shellQml, out TestShell shell)
    {
        shell = new TestShell(("shell.qml", shellQml));
        return shell.Load();
    }

    public object? Eval(QObject scope, string expression)
    {
        var fn = Engine.Js.Compile("return (" + expression + ");", [], new ScopeObject(Engine.Js, scope, scope.CreationContext!), "test");
        return ValueConverter.FromJs(Engine.Js.Engine.Call(fn, Engine.Js.Wrap(scope), []));
    }

    public void Dispose()
    {
        _engine?.Dispose();
        try
        {
            System.IO.Directory.Delete(Directory, true);
        }
        catch (IOException)
        {
        }
    }
}
