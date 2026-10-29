using System.Diagnostics;
using System.Text;
using Jint;
using Jint.Native;

namespace Winshell.Runtime.Shell;

/// <summary>
/// The <c>DataStream</c>: a source of bytes. Nothing is read until <see cref="Parser"/> is set.
/// <see cref="SocketObject"/> is one.
/// </summary>
public class DataStream : QObject
{
    private DataStreamParser? _parser;
    private readonly StringBuilder _buffer = new();

    [Qml]
    public DataStreamParser? Parser
    {
        get { Track(); return _parser; }
        set
        {
            if (!SetField(ref _parser, value) || value is null)
                return;
            value.SetOwner(this);
            if (_buffer.Length == 0)
                return;
            var pending = _buffer.ToString();
            _buffer.Clear();
            value.Feed(pending);
        }
    }

    /// <summary>Hands text to the parser, or holds it until a parser is set.</summary>
    protected void Deliver(string data)
    {
        if (_parser is null)
            _buffer.Append(data);
        else
            _parser.Feed(data);
    }

    /// <summary>Tells the parser the source has closed.</summary>
    protected void EndStream() => _parser?.End();
}

/// <summary>The <c>DataStreamParser</c>: takes the bytes of a stream as they arrive.</summary>
[QmlSignal("read", "data")]
public abstract class DataStreamParser : QObject
{
    /// <summary>New data arrived.</summary>
    public abstract void Feed(string data);

    /// <summary>The stream ended.</summary>
    public virtual void End()
    {
    }

    /// <summary>A new run of the process starts.</summary>
    public virtual void Reset()
    {
    }
}

/// <summary>The <c>SplitParser</c>: emits <c>read</c> once per piece between markers, by default per line.</summary>
public sealed class SplitParser : DataStreamParser
{
    private string _splitMarker = "\n";
    private readonly StringBuilder _buffer = new();
    /// <summary>A CR that ended the previous chunk, so a following LF is one line break.</summary>
    private bool _heldCr;

    [Qml]
    public string SplitMarker
    {
        get { Track(); return _splitMarker; }
        set => SetField(ref _splitMarker, value ?? "\n");
    }

    public override void Feed(string data)
    {
        if (_splitMarker == "\n")
            data = NormalizeNewlines(data);
        _buffer.Append(data);
        Drain();
    }

    /// <summary>
    /// Turns CRLF into LF, including a CR that ended the previous chunk. The buffer is scanned once per chunk.
    /// </summary>
    private string NormalizeNewlines(string data)
    {
        if (_heldCr)
        {
            _heldCr = false;
            data = data.Length > 0 && data[0] == '\n' ? data : "\r" + data;
        }
        if (data.Length > 0 && data[^1] == '\r')
        {
            _heldCr = true;
            data = data[..^1];
        }
        return data.Replace("\r\n", "\n");
    }

    private void Drain()
    {
        if (_splitMarker.Length == 0)
        {
            if (_buffer.Length == 0)
                return;
            var all = _buffer.ToString();
            _buffer.Clear();
            Emit("read", all);
            return;
        }
        var text = _buffer.ToString();
        var start = 0;
        while (true)
        {
            var index = text.IndexOf(_splitMarker, start, StringComparison.Ordinal);
            if (index < 0)
                break;
            Emit("read", text[start..index]);
            start = index + _splitMarker.Length;
        }
        if (start == 0)
            return;
        _buffer.Clear();
        if (start < text.Length)
            _buffer.Append(text, start, text.Length - start);
    }

    public override void End()
    {
        if (_heldCr)
        {
            _buffer.Append('\r');
            _heldCr = false;
        }
        if (_buffer.Length > 0)
        {
            var rest = _buffer.ToString();
            _buffer.Clear();
            Emit("read", rest);
        }
    }

    public override void Reset()
    {
        _buffer.Clear();
        _heldCr = false;
    }
}

/// <summary>The <c>StdioCollector</c>: collects a whole stream as text.</summary>
[QmlSignal("streamFinished")]
public sealed class StdioCollector : DataStreamParser
{
    private readonly StringBuilder _buffer = new();
    private string _text = "";
    private bool _waitForEnd = true;

    [Qml]
    public bool WaitForEnd
    {
        get { Track(); return _waitForEnd; }
        set => SetField(ref _waitForEnd, value);
    }

    [Qml]
    public string Text
    {
        get { Track(); return _text; }
    }

    [Qml]
    public object Data
    {
        get { Track("text"); return _text; }
    }

    public override void Feed(string data)
    {
        _buffer.Append(data);
        if (!_waitForEnd)
            Publish();
    }

    public override void End()
    {
        Publish();
        Emit("streamFinished");
    }

    public override void Reset()
    {
        _buffer.Clear();
    }

    private void Publish()
    {
        var text = _buffer.ToString();
        if (_text == text)
            return;
        _text = text;
        NotifyChanged("text");
        NotifyChanged("data");
    }
}

/// <summary>The <c>Process</c>.</summary>
[QmlSignal("started")]
[QmlSignal("exited", "exitCode", "exitStatus")]
public sealed class ProcessObject : QObject
{
    private List<string> _command = [];
    private bool _running;
    private object? _environment;
    private bool _clearEnvironment;
    private string? _workingDirectory;
    private bool _stdinEnabled;
    private DataStreamParser? _stdout;
    private DataStreamParser? _stderr;
    private Process? _process;
    private int? _processId;
    private int _generation;

    [QmlEnum]
    public enum ExitStatus
    {
        NormalExit = 0,
        CrashExit = 1,
    }

    [Qml]
    public object Command
    {
        get { Track(); return _command; }
        set
        {
            _command = ValueConverter.ToList(value).Select(v => ValueConverter.ToQmlString(v)).ToList();
            NotifyChanged("command");
        }
    }

    [Qml]
    public bool Running
    {
        get { Track(); return _running; }
        set
        {
            if (value == _running)
                return;
            if (value)
            {
                if (IsComplete)
                    Start();
                else
                    SetField(ref _running, true);
            }
            else
            {
                Terminate();
            }
        }
    }

    [Qml]
    public object? Environment
    {
        get { Track(); return _environment; }
        set
        {
            _environment = value is JsValue js ? ValueConverter.KeepJs(js) : value;
            NotifyChanged("environment");
        }
    }

    [Qml]
    public bool ClearEnvironment { get { Track(); return _clearEnvironment; } set => SetField(ref _clearEnvironment, value); }

    [Qml]
    public string WorkingDirectory
    {
        get { Track(); return _workingDirectory ?? System.Environment.CurrentDirectory; }
        set => SetField(ref _workingDirectory, value);
    }

    [Qml]
    public bool StdinEnabled
    {
        get { Track(); return _stdinEnabled; }
        set
        {
            if (!SetField(ref _stdinEnabled, value))
                return;
            if (!value && _process is { HasExited: false })
            {
                try
                {
                    _process.StandardInput.Close();
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
    }

    [Qml]
    public DataStreamParser? Stdout
    {
        get { Track(); return _stdout; }
        set
        {
            if (SetField(ref _stdout, value))
                value?.SetOwner(this);
        }
    }

    [Qml]
    public DataStreamParser? Stderr
    {
        get { Track(); return _stderr; }
        set
        {
            if (SetField(ref _stderr, value))
                value?.SetOwner(this);
        }
    }

    [Qml]
    public object? ProcessId
    {
        get { Track(); return _processId; }
    }

    public override void ComponentComplete()
    {
        if (_running)
        {
            _running = false;
            Start();
        }
    }

    private ProcessSpec Spec()
    {
        var env = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        switch (_environment)
        {
            case JsValue js when js.IsObject():
                var obj = js.AsObject();
                foreach (var key in obj.GetOwnPropertyKeys())
                {
                    var v = obj.Get(key);
                    env[key.ToString()] = v.IsNull() || v.IsUndefined() ? null : v.ToString();
                }
                break;
            case IDictionary<string, object?> dict:
                foreach (var (k, v) in dict)
                    env[k] = v?.ToString();
                break;
        }
        return new ProcessSpec(_command, env, _clearEnvironment, _workingDirectory);
    }

    private void Start()
    {
        if (_command.Count == 0 || Engine is null)
        {
            Engine?.Log.Warn("process", "Cannot start process as command is empty.");
            return;
        }
        var generation = ++_generation;
        var loop = Engine.Loop;
        var spec = Spec();
        Process process;
        try
        {
            process = new Process { StartInfo = ProcessLauncher.CreateStartInfo(spec, redirect: true), EnableRaisingEvents = true };
            process.Start();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            Engine.Log.Warn("process", $"Failed to start process {string.Join(' ', _command)}: {e.Message}");
            loop.Post(() =>
            {
                if (generation != _generation || IsDestroyed)
                    return;
                SetField(ref _running, false, nameof(Running));
                Emit("exited", -1, (int)ExitStatus.CrashExit);
            });
            return;
        }
        _process = process;
        _stdout?.Reset();
        _stderr?.Reset();
        SetField(ref _running, true, nameof(Running));
        _processId = process.Id;
        NotifyChanged("processId");
        if (!_stdinEnabled)
        {
            try
            {
                process.StandardInput.Close();
            }
            catch (IOException)
            {
            }
        }
        Emit("started");

        var outDone = Pump(process.StandardOutput, () => _stdout, generation, loop);
        var errDone = Pump(process.StandardError, () => _stderr, generation, loop);
        _ = Task.WhenAll(outDone, errDone).ContinueWith(_ =>
        {
            process.WaitForExit();
            var code = 0;
            try
            {
                code = process.ExitCode;
            }
            catch (InvalidOperationException)
            {
            }
            loop.Post(() =>
            {
                if (generation != _generation || IsDestroyed)
                    return;
                _process = null;
                _processId = null;
                NotifyChanged("processId");
                SetField(ref _running, false, nameof(Running));
                Emit("exited", code, _killed ? (int)ExitStatus.CrashExit : (int)ExitStatus.NormalExit);
                _killed = false;
            });
            process.Dispose();
        }, TaskScheduler.Default);
    }

    private bool _killed;

    private Task Pump(StreamReader reader, Func<DataStreamParser?> parser, int generation, IEventLoop loop) => Task.Run(async () =>
    {
        var buffer = new char[8192];
        try
        {
            while (true)
            {
                var n = await reader.ReadAsync(buffer).ConfigureAwait(false);
                if (n <= 0)
                    break;
                var chunk = new string(buffer, 0, n);
                loop.Post(() =>
                {
                    if (generation == _generation && !IsDestroyed)
                        parser()?.Feed(chunk);
                });
            }
        }
        catch (IOException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        loop.Post(() =>
        {
            if (generation == _generation && !IsDestroyed)
                parser()?.End();
        });
    });

    private void Terminate()
    {
        var process = _process;
        if (process is null)
        {
            SetField(ref _running, false, nameof(Running));
            return;
        }
        try
        {
            // Windows has no SIGTERM; ask a windowed program to close, and end the rest.
            if (!process.HasExited && !process.CloseMainWindow())
            {
                _killed = true;
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    [Qml]
    public void Signal(int signal)
    {
        var process = _process;
        if (process is null)
            return;
        try
        {
            if (signal is 9 or 15 or 2 or 1)
            {
                _killed = signal == 9;
                if (signal == 9 || !process.CloseMainWindow())
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (System.ComponentModel.Win32Exception)
        {
        }
    }

    [Qml]
    public void Write(string data)
    {
        if (_process is null || !_stdinEnabled)
            return;
        try
        {
            _process.StandardInput.Write(data);
            _process.StandardInput.Flush();
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    [Qml]
    public void StartDetached()
    {
        if (_command.Count == 0)
            return;
        ProcessLauncher.StartDetached(Spec(), Engine?.Log ?? Log.Default);
    }

    /// <summary>Sets the command and environment from a context, then starts, as <c>exec()</c> does.</summary>
    [Qml]
    public void Exec(object? context)
    {
        var spec = ProcessSpec.From(context);
        Terminate();
        _command = spec.Command.ToList();
        NotifyChanged("command");
        if (spec.Environment.Count > 0)
            _environment = spec.Environment.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
        _clearEnvironment = spec.ClearEnvironment;
        if (spec.WorkingDirectory is not null)
            _workingDirectory = spec.WorkingDirectory;
        _generation++;
        _process = null;
        _running = false;
        Start();
    }

    protected override void OnDestroy()
    {
        _generation++;
        if (_process is { } p)
        {
            try
            {
                if (!p.HasExited)
                    p.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
        }
    }
}

/// <summary>The <c>FileViewAdapter</c>: turns a file's text into properties and back.</summary>
[QmlSignal("adapterUpdated")]
public abstract class FileViewAdapter : QObject
{
    internal FileView? View { get; set; }

    /// <summary>Set by <see cref="Serialize"/> when the bytes on disk must be left as they are.</summary>
    internal bool PreserveFile { get; set; }

    public abstract void Deserialize(string text);

    public abstract string Serialize();
}

/// <summary>The <c>JsonObject</c>: a nested object in a JsonAdapter.</summary>
public class JsonObject : QObject, IPropertyObserver
{
    internal Action? Changed { get; set; }

    private bool _loading;

    public override void ComponentComplete()
    {
        foreach (var p in DynamicProperties)
            AddObserver(p.Name, this);
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
        if (!_loading)
            Changed?.Invoke();
    }

    internal void Load(JsValue value, QmlEngine engine)
    {
        if (!value.IsObject())
            return;
        _loading = true;
        try
        {
            var obj = value.AsObject();
            foreach (var p in DynamicProperties)
            {
                if (p.IsReadonly)
                    continue;
                var v = obj.Get(p.Name);
                if (v.IsUndefined())
                    continue;
                var current = p.Read();
                if (current is JsonObject nested)
                {
                    nested.Load(v, engine);
                    continue;
                }
                p.Write(v);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    internal JsValue Save(QmlEngine engine)
    {
        var obj = new JsObject(engine.Js.Engine);
        foreach (var p in DynamicProperties)
        {
            var v = p.Read();
            obj.Set(p.Name, v is JsonObject nested ? nested.Save(engine) : engine.Js.ToJs(v));
        }
        return obj;
    }
}

/// <summary>The <c>JsonAdapter</c>: a JSON file as properties.</summary>
public sealed class JsonAdapter : FileViewAdapter, IPropertyObserver
{
    private readonly JsonObject _root = new();
    private bool _loading;

    public override void ComponentComplete()
    {
        foreach (var p in DynamicProperties)
        {
            AddObserver(p.Name, this);
            if (p.Read() is JsonObject nested)
                nested.Changed = () => Emit("adapterUpdated");
        }
        _ = _root;
    }

    public void OnPropertyChanged(QObject sender, string property)
    {
        // The view passes it on, so it reaches the view once.
        if (!_loading)
            Emit("adapterUpdated");
    }

    public override void Deserialize(string text)
    {
        if (Engine is null || string.IsNullOrWhiteSpace(text))
            return;
        JsValue parsed;
        try
        {
            parsed = new Jint.Native.Json.JsonParser(Engine.Js.Engine).Parse(text);
        }
        catch (Jint.Runtime.JavaScriptException e)
        {
            Engine.Log.Warn("io", $"JsonAdapter: failed to parse JSON: {e.Message}");
            return;
        }
        if (!parsed.IsObject())
            return;
        _loading = true;
        try
        {
            var obj = parsed.AsObject();
            foreach (var p in DynamicProperties)
            {
                if (p.IsReadonly)
                    continue;
                var v = obj.Get(p.Name);
                if (v.IsUndefined())
                    continue;
                if (p.Read() is JsonObject nested)
                    nested.Load(v, Engine);
                else
                    p.Write(v);
            }
        }
        finally
        {
            _loading = false;
        }
    }

    public override string Serialize()
    {
        if (Engine is null)
            return "{}";
        var obj = new JsObject(Engine.Js.Engine);
        foreach (var p in DynamicProperties)
        {
            var v = p.Read();
            obj.Set(p.Name, v is JsonObject nested ? nested.Save(Engine) : Engine.Js.ToJs(v));
        }
        return new Jint.Native.Json.JsonSerializer(Engine.Js.Engine).Serialize(obj, JsValue.Undefined, "  ").ToString();
    }
}

/// <summary>The <c>FileView</c>: reads, watches and writes a file.</summary>
[QmlDefaultProperty("adapter")]
[QmlSignal("loaded")]
[QmlSignal("loadFailed", "error")]
[QmlSignal("fileChanged")]
[QmlSignal("saved")]
[QmlSignal("saveFailed", "error")]
[QmlSignal("adapterUpdated")]
public sealed class FileView : QObject
{
    private string _path = "";
    private bool _preload = true;
    private bool _blockLoading;
    private bool _blockWrites;
    private bool _blockAllReads;
    private bool _watchChanges;
    private bool _atomicWrites = true;
    private bool _printErrors = true;
    private bool _loaded;
    private string? _text;
    private FileViewAdapter? _adapter;
    private FileSystemWatcher? _watcher;
    private string? _watchedPath;
    private int _generation;
    private bool _bom;

    /// <summary>
    /// Tests only. Runs on the writing thread after a temporary file is in place and before it replaces the
    /// destination, so a test can land a hand save in that window. A write on another thread does not see it.
    /// </summary>
    [ThreadStatic]
    private static Action<string>? _beforeReplace;

    internal static void SetBeforeReplaceForTests(Action<string>? probe) => _beforeReplace = probe;

    /// <summary>
    /// A hand save landed during this write when the destination changed after the write started and is not the
    /// text about to be put down. <paramref name="diskAtStart"/> null means the file was missing.
    /// </summary>
    internal static bool HandSaveWins(string? onDisk, string? diskAtStart, string incoming)
        => onDisk is not null
            && !string.Equals(onDisk, diskAtStart, StringComparison.Ordinal)
            && !string.Equals(onDisk, incoming, StringComparison.Ordinal);

    [QmlEnum]
    public enum FileViewError
    {
        Success = 0,
        Unknown = 1,
        FileNotFound = 2,
        PermissionDenied = 3,
        NotAFile = 4,
    }

    [Qml]
    public string Path
    {
        get { Track(); return _path; }
        set
        {
            var path = value ?? "";
            if (path.StartsWith("file://", StringComparison.OrdinalIgnoreCase))
                path = Quick.ImageLoader.LocalPath(path, null) ?? path;
            if (!SetField(ref _path, path))
                return;
            _text = null;
            SetLoaded(false);
            Watch();
            if (IsComplete && _preload)
                Load(sync: _blockLoading);
        }
    }

    [Qml] public bool Preload { get { Track(); return _preload; } set => SetField(ref _preload, value); }

    [Qml] public bool BlockLoading { get { Track(); return _blockLoading; } set => SetField(ref _blockLoading, value); }

    [Qml] public bool BlockWrites { get { Track(); return _blockWrites; } set => SetField(ref _blockWrites, value); }

    [Qml] public bool BlockAllReads { get { Track(); return _blockAllReads; } set => SetField(ref _blockAllReads, value); }

    [Qml] public bool AtomicWrites { get { Track(); return _atomicWrites; } set => SetField(ref _atomicWrites, value); }

    [Qml] public bool PrintErrors { get { Track(); return _printErrors; } set => SetField(ref _printErrors, value); }

    [Qml]
    public bool WatchChanges
    {
        get { Track(); return _watchChanges; }
        set
        {
            if (SetField(ref _watchChanges, value))
                Watch();
        }
    }

    [Qml("loaded")]
    public bool IsLoaded
    {
        get { Track("loaded"); return _loaded; }
    }

    [Qml]
    public FileViewAdapter? Adapter
    {
        get { Track(); return _adapter; }
        set
        {
            if (!SetField(ref _adapter, value))
                return;
            if (value is not null)
            {
                value.SetOwner(this);
                value.View = this;
                value.Connect("adapterUpdated", _ => Emit("adapterUpdated"));
                if (_text is not null)
                    value.Deserialize(_text);
            }
        }
    }

    private void SetLoaded(bool value)
    {
        if (SetField(ref _loaded, value, "loaded") && value)
            Emit("loaded");
    }

    public override void ComponentComplete()
    {
        if (_path.Length > 0 && _preload)
            Load(sync: _blockLoading || _blockAllReads);
    }

    /// <summary>The text; reads the file now if it was not read yet.</summary>
    [Qml("text")]
    public string GetText()
    {
        TrackQml("text");
        if (_text is null && _path.Length > 0)
            Load(sync: true);
        return _text ?? "";
    }

    [Qml("data")]
    public string GetData() => GetText();

    /// <summary>The text last read or written, without reading the file.</summary>
    internal string? Text => _text;

    [Qml]
    public void Reload() => Load(sync: _blockLoading || _blockAllReads);

    [Qml]
    public bool WaitForJob() => true;

    private void Load(bool sync)
    {
        if (_path.Length == 0 || Engine is null)
            return;
        var generation = ++_generation;
        var path = _path;
        if (sync)
        {
            Apply(generation, Read(path));
            return;
        }
        var loop = Engine.Loop;
        Task.Run(() => Read(path)).ContinueWith(t => loop.Post(() => Apply(generation, t.Result)), TaskScheduler.Default);
    }

    private static (string? Text, bool Bom, FileViewError Error, string Message) Read(string path)
    {
        try
        {
            if (Directory.Exists(path))
                return (null, false, FileViewError.NotAFile, "not a file");
            // Share the file so a save can replace it, and a test can delete it, while this read is open.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            Span<byte> start = stackalloc byte[3];
            var count = stream.Read(start);
            var bom = count == 3 && start[0] == 0xEF && start[1] == 0xBB && start[2] == 0xBF;
            stream.Position = 0;
            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return (reader.ReadToEnd(), bom, FileViewError.Success, "");
        }
        catch (FileNotFoundException)
        {
            return (null, false, FileViewError.FileNotFound, "file not found");
        }
        catch (DirectoryNotFoundException)
        {
            return (null, false, FileViewError.FileNotFound, "file not found");
        }
        catch (UnauthorizedAccessException)
        {
            return (null, false, FileViewError.PermissionDenied, "permission denied");
        }
        catch (IOException e)
        {
            return (null, false, FileViewError.Unknown, e.Message);
        }
    }

    private static void WriteText(string path, string text, bool bom)
    {
        if (!bom)
        {
            File.WriteAllText(path, text);
            return;
        }
        File.WriteAllText(path, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private void Apply(int generation, (string? Text, bool Bom, FileViewError Error, string Message) result)
    {
        if (generation != _generation || IsDestroyed)
            return;
        if (result.Text is null)
        {
            if (_printErrors && result.Error != FileViewError.FileNotFound)
                Engine?.Log.Warn("io", $"FileView: failed to read {_path}: {result.Message}");
            _text = null;
            _bom = false;
            SetLoaded(false);
            Emit("loadFailed", (int)result.Error);
            return;
        }
        var changed = _text != result.Text;
        _bom = result.Bom;
        _text = result.Text;
        if (changed)
            NotifyChanged("text");
        _adapter?.Deserialize(_text);
        _loaded = false;
        SetLoaded(true);
    }

    [Qml]
    public void SetText(string text) => Write(text);

    [Qml]
    public void SetData(object? data) => Write(ValueConverter.ToQmlString(data));

    [Qml]
    public void WriteAdapter()
    {
        if (_adapter is null)
            return;
        var text = _adapter.Serialize();
        if (_adapter.PreserveFile)
            return;
        Write(text);
    }

    private void Write(string text)
    {
        if (_path.Length == 0)
            return;
        var path = _path;
        var atomic = _atomicWrites;
        var bom = _bom;
        var loop = Engine?.Loop;
        _text = text;
        NotifyChanged("text");
        void DoWrite()
        {
            string? error = null;
            var handSaveWon = false;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!);
                // Captured before the temporary file exists. A hand save during that write changes the destination
                // after this, which is the race. A change that is already on disk stays writable: TomlAdapter
                // recovers from it and the text above already includes it.
                var diskAtStart = Read(path).Text;
                if (atomic)
                {
                    var temp = path + ".tmp-" + Environment.ProcessId;
                    WriteText(temp, text, bom);
                    try
                    {
                        // A slow scan of the temporary file is when a hand save usually lands.
                        _beforeReplace?.Invoke(path);
                        if (HandSaveWins(Read(path).Text, diskAtStart, text))
                        {
                            handSaveWon = true;
                        }
                        else
                        {
                            File.Move(temp, path, overwrite: true);
                        }
                    }
                    finally
                    {
                        try
                        {
                            if (handSaveWon && File.Exists(temp))
                                File.Delete(temp);
                        }
                        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                        {
                        }
                    }
                }
                else if (HandSaveWins(Read(path).Text, diskAtStart, text))
                {
                    handSaveWon = true;
                }
                else
                {
                    WriteText(path, text, bom);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                error = e.Message;
            }
            void Report()
            {
                if (IsDestroyed)
                    return;
                if (error is null)
                {
                    // ReadDirectoryChangesW watches the directory, so a rename-replace does not detach it.
                    // Arm only when the directory has just appeared, then read once: a hand save that landed
                    // while this write ran is already on disk and will not produce a later event.
                    Watch();
                    AcceptHandSave(path);
                    if (!handSaveWon)
                        Emit("saved");
                }
                else
                {
                    if (_printErrors)
                        Engine?.Log.Warn("io", $"FileView: failed to write {path}: {error}");
                    Emit("saveFailed", 1);
                }
            }
            if (loop is null)
                Report();
            else
                loop.Post(Report);
        }
        if (_blockWrites || loop is null)
            DoWrite();
        else
            Task.Run(DoWrite);
    }

    private void AcceptHandSave(string path)
    {
        if (IsDestroyed)
            return;
        // Our own write already matches the buffer. A hand save in the same moment does not.
        var read = Read(path);
        if (read.Text is null || read.Text == _text)
            return;
        Emit("fileChanged");
    }

    private void Watch()
    {
        if (!_watchChanges || _path.Length == 0 || Engine is null)
        {
            StopWatching();
            return;
        }
        var full = System.IO.Path.GetFullPath(_path);
        var dir = System.IO.Path.GetDirectoryName(full);
        if (dir is null || !Directory.Exists(dir))
        {
            StopWatching();
            return;
        }
        // Replacing the file does not detach a directory watch. Disposing it here drops a hand save that
        // already happened, and the new watch never sees that event.
        if (_watcher is not null && _watchedPath == full)
            return;
        StopWatching();
        var loop = Engine.Loop;
        var watched = full;
        var watcher = new FileSystemWatcher(dir, System.IO.Path.GetFileName(full))
        {
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime,
            InternalBufferSize = 65536,
        };
        // Read on the loop, after the event. Reading here would hold the file during our own replace.
        void Changed(object? _, FileSystemEventArgs e) => loop.Post(() => AcceptHandSave(watched));
        watcher.Changed += Changed;
        watcher.Created += Changed;
        watcher.Renamed += (s, e) => Changed(s, e);
        watcher.Error += (_, _) => loop.Post(() =>
        {
            if (IsDestroyed || !ReferenceEquals(_watcher, watcher))
                return;
            // The watch dropped an event. Arm it again and apply whatever the file says now.
            StopWatching();
            Watch();
            AcceptHandSave(watched);
        });
        watcher.EnableRaisingEvents = true;
        _watchedPath = full;
        _watcher = watcher;
    }

    private void StopWatching()
    {
        _watcher?.Dispose();
        _watcher = null;
        _watchedPath = null;
    }

    protected override void OnDestroy()
    {
        _generation++;
        StopWatching();
    }
}
