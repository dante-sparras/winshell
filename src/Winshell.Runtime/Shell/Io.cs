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
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or IOException or ArgumentException)
        {
            Engine.Log.Warn("process", $"Failed to start process {_command[0]}: {e.Message}");
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
                _killed = signal == 9 || !process.CloseMainWindow();
                if (_killed)
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
    private string _error = "";

    internal FileView? View { get; set; }

    /// <summary>Set by <see cref="Serialize"/> when the bytes on disk must be left as they are.</summary>
    internal bool PreserveFile { get; set; }

    /// <summary>Why the file could not be parsed, as <c>path[line:column]: message</c>, or empty when the last read parsed. Read-only in QML.</summary>
    [Qml]
    public string Error
    {
        get { Track(); return _error; }
        internal set
        {
            if (SetField(ref _error, value ?? ""))
                View?.NoteAdapterError();
        }
    }

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
        {
            AddObserver(p.Name, this);
            if (p.Read() is JsonObject nested)
                nested.Changed = () => Changed?.Invoke();
        }
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

    /// <summary>Bytes a read will take. Past this, the read is refused and the file is left as it is. 64 MiB.</summary>
    internal int MaxFileBytes { get; set; } = DataLimits.MaxReadBytes;
    private string? _text;
    private FileViewAdapter? _adapter;
    private DirectoryWatch? _watch;
    private int _watchGeneration;
    private string? _watchedPath;
    private bool _watchingParent;
    private int _generation;
    private int _loadSettled;
    private int _writeSerial;
    private int _writeSettled;
    private bool _bom;
    // Tests only. The load or save thread calls this after the file work and before it posts the result.
    private Action<IEventLoop>? _jobGate;
    private Thread? _loadThread;
    private Thread? _writeThread;
    // One save at a time, on a thread of its own, so a later save cannot finish before an earlier one and a burst
    // does not take a thread pool thread per save. Saves still waiting are kept as the latest text.
    private readonly object _writeGate = new();
    private Task _writes = Task.CompletedTask;
    private QueuedSave? _queued;
    private bool _draining;
    private int _reserved;
    // Link kind of the last destination this view saved. Counting links opens the file, and a burst of saves
    // would hold that open across the next replace. A hand save forgets it: the file may now be a different link.
    private string? _linkKindPath;
    private bool _linkKindHard;
    private volatile bool _linkKindKnown;

    /// <summary>
    /// Tests only. Runs on the writing thread after a temporary file is in place and before it replaces the
    /// destination, so a test can land a hand save in that window. A write on another thread does not see it.
    /// </summary>
    [ThreadStatic]
    private static Action<string>? _beforeReplace;

    internal static void SetBeforeReplaceForTests(Action<string>? probe) => _beforeReplace = probe;

    /// <summary>
    /// Tests only. The background load or save calls this after the file work and before it posts
    /// <c>loaded</c>, <c>loadFailed</c>, <c>saved</c> or <c>saveFailed</c>. A test blocks here until another thread releases it.
    /// </summary>
    internal void SetJobGateForTests(Action<IEventLoop>? gate) => _jobGate = gate;

    /// <summary>Tests only. Waits until the background load and save threads have exited.</summary>
    internal bool JoinJobsForTests(TimeSpan timeout)
    {
        var ok = true;
        if (_loadThread is { IsAlive: true } load)
            ok &= load.Join(timeout);
        if (_writeThread is { IsAlive: true } write)
            ok &= write.Join(timeout);
        return ok;
    }

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
            _linkKindKnown = false;
            ClearReportedError();
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

    /// <summary>
    /// Blocks until the load or save already in progress finishes, so <see cref="GetText"/> is current when this returns.
    /// Returns false when nothing is running, or this view is destroyed before the call returns.
    /// The job raises <c>loaded</c>, <c>loadFailed</c>, <c>saved</c> or <c>saveFailed</c> before this returns.
    /// </summary>
    [Qml]
    public bool WaitForJob()
    {
        var loadTarget = _generation;
        var writeTarget = _writeSerial;
        if (_loadSettled >= loadTarget && _writeSettled >= writeTarget)
            return false;
        return WaitUntil(loadTarget, writeTarget);
    }

    /// <summary>
    /// The job posts its result to this loop, and that post is what raises the signal. Waiting without running
    /// posted work would deadlock when the job's thread needs this thread to finish. Timers stay queued so they
    /// do not fire early. Work the signal posts is not in the batch that raised it, so it waits until this returns.
    /// </summary>
    private bool WaitUntil(int loadTarget, int writeTarget)
    {
        if (Engine?.Loop is not EventQueue queue)
            return false;
        var gate = new object();
        var signaled = false;
        void OnWake()
        {
            lock (gate)
            {
                signaled = true;
                Monitor.PulseAll(gate);
            }
        }
        queue.Woken += OnWake;
        try
        {
            while (!IsDestroyed && (_loadSettled < loadTarget || _writeSettled < writeTarget))
            {
                lock (gate)
                    signaled = false;
                if (queue.RunPosted())
                {
                    if (IsDestroyed)
                        return false;
                    continue;
                }
                if (IsDestroyed || (_loadSettled >= loadTarget && _writeSettled >= writeTarget))
                    break;
                lock (gate)
                {
                    if (signaled || IsDestroyed || _loadSettled >= loadTarget && _writeSettled >= writeTarget)
                        continue;
                    Monitor.Wait(gate);
                }
            }
        }
        finally
        {
            queue.Woken -= OnWake;
        }
        return !IsDestroyed && _loadSettled >= loadTarget && _writeSettled >= writeTarget;
    }

    private void SettleLoad(int generation)
    {
        if (generation > _loadSettled)
            _loadSettled = generation;
    }

    private void SettleWrite(int serial)
    {
        if (serial > _writeSettled)
            _writeSettled = serial;
    }

    private void Load(bool sync)
    {
        if (_path.Length == 0 || Engine is null)
            return;
        var generation = ++_generation;
        var path = _path;
        var maxFileBytes = MaxFileBytes;
        if (sync)
        {
            Apply(generation, Read(path, maxFileBytes));
            return;
        }
        var loop = Engine.Loop;
        // A pool thread can sit unscheduled while this thread waits for the read, which is the deadlock
        // waitForJob has to avoid. One thread for this read posts the result and exits.
        var thread = new Thread(() =>
        {
            (string? Text, bool Bom, FileViewError Error, string Message) result;
            try
            {
                result = Read(path, maxFileBytes);
                _jobGate?.Invoke(loop);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                result = (null, false, FileViewError.Unknown, e.Message);
            }
            loop.Post(() => Apply(generation, result));
        })
        { IsBackground = true };
        _loadThread = thread;
        thread.Start();
    }

    private static (string? Text, bool Bom, FileViewError Error, string Message) Read(string path, int maxFileBytes)
    {
        try
        {
            if (Directory.Exists(path))
                return (null, false, FileViewError.NotAFile, "not a file");
            // Share the file so a save can replace it, and a test can delete it, while this read is open.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            // The length is known before the bytes are. A file past the limit is not copied into a string.
            if (DataLimits.FileBytes(stream.Length, maxFileBytes) is { } why)
                return (null, false, FileViewError.Unknown, why);
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

    private static FileViewError WriteError(Exception exception) => exception switch
    {
        UnauthorizedAccessException => FileViewError.PermissionDenied,
        FileNotFoundException or DirectoryNotFoundException => FileViewError.FileNotFound,
        _ => FileViewError.Unknown,
    };

    /// <summary>
    /// Same destination as <see cref="SaveTarget"/>, remembered for this view so the next save does not open the
    /// file again just to count links.
    /// </summary>
    private string DestinationForSave(string path, out bool hardLink)
    {
        var full = AtomicFile.FollowLink(path);
        if (_linkKindKnown && string.Equals(_linkKindPath, full, StringComparison.Ordinal))
        {
            hardLink = _linkKindHard;
            return full;
        }
        // Written before the flag, so a reader that sees the flag also sees this path and kind.
        hardLink = File.Exists(full) && AtomicFile.LinkCount(full) > 1;
        _linkKindPath = full;
        _linkKindHard = hardLink;
        _linkKindKnown = true;
        return full;
    }

    private void Apply(int generation, (string? Text, bool Bom, FileViewError Error, string Message) result)
    {
        // Before the signal, so a handler that calls waitForJob does not wait for this same load.
        SettleLoad(generation);
        if (generation != _generation || IsDestroyed)
            return;
        if (result.Text is null)
        {
            // A file past the limit is still the user's file. Dropping the text already read would make the next
            // save replace it with the values from before.
            if (DataLimits.IsFileLimit(result.Message))
            {
                if (_adapter is not null)
                    _adapter.Error = $"{_path}: {result.Message}";
                Engine?.Log.Warn("io", $"FileView: failed to read {_path}: {result.Message}");
                Emit("loadFailed", (int)result.Error);
                return;
            }
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
        // Merging into a file we will not read would replace it. The values in use stay in memory.
        if (DataLimits.FileBytes(FileLength(_path), MaxFileBytes) is { } why)
        {
            _adapter.Error = $"{_path}: {why}";
            Engine?.Log.Warn("io", $"FileView: failed to write {_path}: {why}");
            Emit("saveFailed", (int)FileViewError.Unknown);
            return;
        }
        var text = _adapter.Serialize();
        if (_adapter.PreserveFile)
        {
            // The file on disk does not parse, so the write was dropped. Say so; the log line is not enough.
            Emit("saveFailed", (int)FileViewError.Unknown);
            return;
        }
        Write(text);
    }

    private static long FileLength(string path)
    {
        try
        {
            return new FileInfo(path).Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return -1;
        }
    }

    private void Write(string text)
    {
        if (_path.Length == 0)
            return;
        var save = new QueuedSave(_path, text, _atomicWrites, _bom, Engine?.Loop, ++_writeSerial);
        _text = text;
        NotifyChanged("text");
        // A blocking save runs on this thread, so a test probe set here still runs. Async saves share one thread
        // that is not taken from the pool: a burst of Task.Run calls, each blocked on the save before it, left the
        // first save waiting for a worker that the busy pool was slow to add.
        var wait = _blockWrites || save.Loop is null;
        if (wait)
        {
            Task prior;
            TaskCompletionSource ready;
            lock (_writeGate)
            {
                _reserved++;
                prior = _writes;
                ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _writes = ready.Task;
            }
            try
            {
                WaitForSave(prior);
                WriteQueued(save, background: false);
            }
            finally
            {
                lock (_writeGate)
                {
                    _reserved--;
                    ready.TrySetResult();
                    StartDrain();
                }
            }
        }
        else
        {
            lock (_writeGate)
            {
                _queued = save;
                StartDrain();
            }
        }
    }

    /// <summary>Starts the drain if a save is waiting and no blocking save has reserved the next turn. The caller holds <see cref="_writeGate"/>.</summary>
    private void StartDrain()
    {
        if (_draining || _reserved > 0 || _queued is null)
            return;
        _draining = true;
        var prior = _writes;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _writes = done.Task;
        var thread = new Thread(() =>
        {
            try
            {
                WaitForSave(prior);
                while (true)
                {
                    QueuedSave next;
                    lock (_writeGate)
                    {
                        if (_reserved > 0 || _queued is null)
                            return;
                        next = _queued;
                        _queued = null;
                    }
                    WriteQueued(next, background: true);
                }
            }
            finally
            {
                lock (_writeGate)
                {
                    _draining = false;
                    done.TrySetResult();
                    StartDrain();
                }
            }
        })
        { IsBackground = true };
        _writeThread = thread;
        thread.Start();
    }

    private static void WaitForSave(Task prior)
    {
        try
        {
            prior.GetAwaiter().GetResult();
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
        }
    }

    private void WriteQueued(QueuedSave save, bool background)
    {
        var path = save.Path;
        var text = save.Text;
        var maxFileBytes = MaxFileBytes;
        FileViewError? failure = null;
        var message = "";
        var handSaveWon = false;
        var destination = path;
        try
        {
            destination = DestinationForSave(path, out var hardLink);
            if (Directory.Exists(destination))
            {
                failure = FileViewError.NotAFile;
                message = "not a file";
            }
            else
            {
                var directory = System.IO.Path.GetDirectoryName(destination);
                if (directory is not null)
                    Directory.CreateDirectory(directory);
                AtomicFile.DeleteStaleTemps(destination);
                // Captured before the temporary file exists. A hand save during that write changes the destination
                // after this, which is the race. A change that is already on disk stays writable: TomlAdapter
                // recovers from it and the text above already includes it.
                var diskAtStart = Read(destination, maxFileBytes).Text;
                // A hard link shares its bytes with another name. Replacing the name would break that link.
                var inPlace = !save.Atomic || hardLink;
                handSaveWon = !AtomicFile.Write(destination, text, save.Bom, inPlace, () =>
                {
                    // A slow scan of the temporary file is when a hand save usually lands.
                    _beforeReplace?.Invoke(destination);
                    return AtomicFile.HandSaveWins(Read(destination, maxFileBytes).Text, diskAtStart, text);
                });
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            failure = WriteError(e);
            message = e.Message;
        }
        void Report()
        {
            // Before the signal, so a handler that calls waitForJob does not wait for this same save.
            SettleWrite(save.Serial);
            if (IsDestroyed)
                return;
            if (failure is null)
            {
                // ReadDirectoryChangesW watches the directory, so a rename-replace does not detach it.
                // Arm only when the directory has just appeared, then read once: a hand save that landed
                // while this write ran is already on disk and will not produce a later event.
                Watch();
                AcceptHandSave(destination);
                if (!handSaveWon)
                    Emit("saved");
            }
            else
            {
                if (_printErrors)
                    Engine?.Log.Warn("io", $"FileView: failed to write {path}: {message}");
                Emit("saveFailed", (int)failure.Value);
            }
        }
        // The save thread is past the file work and has not posted the signal yet. A test holds it here.
        // The signal is still posted if that gate throws, so a waiter is not left blocked.
        try
        {
            if (background && save.Loop is not null)
                _jobGate?.Invoke(save.Loop);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            if (failure is null)
            {
                failure = FileViewError.Unknown;
                message = e.Message;
            }
        }
        if (save.Loop is null)
            Report();
        else
            save.Loop.Post(Report);
    }

    private sealed class QueuedSave(string path, string text, bool atomic, bool bom, IEventLoop? loop, int serial)
    {
        public string Path { get; } = path;
        public string Text { get; } = text;
        public bool Atomic { get; } = atomic;
        public bool Bom { get; } = bom;
        public IEventLoop? Loop { get; } = loop;
        public int Serial { get; } = serial;
    }

    private void AcceptHandSave(string path)
    {
        if (IsDestroyed)
            return;
        // Our own write already matches the buffer. A hand save in the same moment does not.
        var read = Read(path, MaxFileBytes);
        // A file past the limit is still the user's file. Say so, and leave the text already loaded.
        if (read.Text is null && DataLimits.IsFileLimit(read.Message))
        {
            if (_adapter is not null)
                _adapter.Error = $"{path}: {read.Message}";
            Engine?.Log.Warn("io", $"FileView: failed to read {path}: {read.Message}");
            Emit("loadFailed", (int)read.Error);
            return;
        }
        // A deleted file differs from the text this view holds. Any other failed read says nothing about the file.
        var deleted = read.Error == FileViewError.FileNotFound && _text is not null;
        if (!deleted && (read.Text is null || read.Text == _text))
            return;
        // The bytes on disk are not ours. The name may now be a hard link, so the next save has to look again.
        _linkKindKnown = false;
        Emit("fileChanged");
    }

    private void Watch()
    {
        if (!_watchChanges || _path.Length == 0 || Engine is null)
        {
            StopWatching();
            return;
        }
        var full = AtomicFile.FollowLink(_path);
        var dir = System.IO.Path.GetDirectoryName(full);
        var name = System.IO.Path.GetFileName(full);
        var parent = false;
        // A folder that does not exist yet is watched from the nearest one that does. Each folder created
        // under it moves the watch one step closer to the file.
        while (dir is not null && !Directory.Exists(dir))
        {
            parent = true;
            name = System.IO.Path.GetFileName(dir);
            dir = System.IO.Path.GetDirectoryName(dir);
        }
        if (dir is null)
        {
            StopWatching();
            return;
        }
        // Replacing the file does not detach a directory watch. Restarting it here drops a hand save that
        // already happened, and the new watch never sees that event.
        if (_watch is not null && _watchedPath == full && !_watchingParent && !parent)
            return;
        StopWatching();
        var generation = _watchGeneration;
        var loop = Engine.Loop;
        // Read on the loop, after the event. Reading here would hold the file during our own replace.
        Action changed = parent ? () => Rewatch(generation, full) : () => AcceptHandSave(full);
        _watch = DirectoryWatch.Start(dir, parent ? DirectoryWatch.NameChanges : DirectoryWatch.FileChanges, changedName =>
        {
            if (changedName.Equals(name, StringComparison.OrdinalIgnoreCase))
                loop.Post(changed);
        }, () => loop.Post(() => Rewatch(generation, full)));
        if (_watch is null)
            return;
        _watchedPath = full;
        _watchingParent = parent;
        // A folder made after the check above and before the watch started will not be reported.
        if (parent && Directory.Exists(System.IO.Path.Combine(dir, name)))
            loop.Post(changed);
    }

    /// <summary>Watches again after a watched folder appeared or the watch ended.</summary>
    private void Rewatch(int generation, string full)
    {
        if (IsDestroyed || generation != _watchGeneration)
            return;
        StopWatching();
        Watch();
        // The file may have been written before this reached the loop, so the new watch would not see it.
        AcceptHandSave(full);
    }

    private void StopWatching()
    {
        _watchGeneration++;
        _watch?.Dispose();
        _watch = null;
        _watchedPath = null;
        _watchingParent = false;
    }

    internal void NoteAdapterError()
    {
        Engine?.GetService<IShellHost>()?.ReportFileError(this, _adapter?.Error);
    }

    private void ClearReportedError() => Engine?.GetService<IShellHost>()?.ReportFileError(this, null);

    protected override void OnDestroy()
    {
        _generation++;
        ClearReportedError();
        StopWatching();
    }
}
