using System.Globalization;
using System.Net.Sockets;
using System.Text;
using Jint;
using Jint.Native;

namespace Winshell.Runtime.Shell;

/// <summary>The <c>IpcHandler</c>: functions, properties and signals that <c>ipc</c> commands can reach.</summary>
public sealed class IpcHandler : QObject
{
    private bool _enabled = true;
    private string _target = "";

    [Qml]
    public bool Enabled
    {
        get { Track(); return _enabled; }
        set
        {
            if (SetField(ref _enabled, value))
                Register();
        }
    }

    [Qml]
    public string Target
    {
        get { Track(); return _target; }
        set
        {
            var old = _target;
            if (!SetField(ref _target, value ?? ""))
                return;
            Engine?.GetService<IpcRegistry>()?.Unregister(old, this);
            Register();
        }
    }

    public override void ComponentComplete() => Register();

    private void Register()
    {
        if (!IsComplete || Engine?.GetService<IpcRegistry>() is not { } registry)
            return;
        registry.Unregister(_target, this);
        if (_enabled && _target.Length > 0)
            registry.Register(_target, this);
    }

    protected override void OnDestroy() => Engine?.GetService<IpcRegistry>()?.Unregister(_target, this);
}

/// <summary>The IPC handlers of one shell, by target, and the calls the CLI makes to them.</summary>
public sealed class IpcRegistry
{
    private static readonly HashSet<string> IpcTypes = ["string", "int", "bool", "real", "double", "color", "void"];
    private readonly Dictionary<string, IpcHandler> _handlers = new(StringComparer.Ordinal);

    public void Register(string target, IpcHandler handler)
    {
        if (_handlers.TryGetValue(target, out var existing) && existing != handler)
            handler.Engine?.Log.Warn("ipc", $"Duplicate IpcHandler target \"{target}\"; the later one wins.");
        _handlers[target] = handler;
    }

    public void Unregister(string target, IpcHandler handler)
    {
        if (_handlers.TryGetValue(target, out var existing) && existing == handler)
            _handlers.Remove(target);
    }

    /// <summary>Every target with its functions, properties and signals, as <c>ipc show</c> prints them.</summary>
    public string Show()
    {
        var sb = new StringBuilder();
        foreach (var (target, handler) in _handlers.OrderBy(h => h.Key, StringComparer.Ordinal))
        {
            sb.Append("target ").Append(target).Append('\n');
            foreach (var name in handler.FunctionNames)
            {
                if (handler.FunctionDeclaration(name) is { } d && Callable(d))
                    sb.Append("  function ").Append(name).Append('(').Append(string.Join(", ", d.Parameters.Select(p => $"{p.Name}: {p.Type}"))).Append("): ").Append(d.ReturnType).Append('\n');
            }
            foreach (var p in handler.DynamicProperties)
            {
                if (IpcTypes.Contains(p.QmlType))
                    sb.Append("  property ").Append(p.Name).Append(": ").Append(p.QmlType).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static bool Callable(Qml.QmlFunctionDeclaration d) =>
        d.Parameters.Count <= 10 && d.ReturnType is not null && IpcTypes.Contains(d.ReturnType) && d.Parameters.All(p => p.Type is not null && IpcTypes.Contains(p.Type) && p.Type != "void");

    /// <summary>Calls a handler function. Returns its result as text, or throws with a message for the CLI.</summary>
    public string Call(string target, string function, IReadOnlyList<string> args)
    {
        if (!_handlers.TryGetValue(target, out var handler))
            throw new IpcException($"Target not found: {target}");
        if (handler.FunctionDeclaration(function) is not { } d || !Callable(d))
            throw new IpcException($"Function not found: {target}.{function}");
        if (args.Count != d.Parameters.Count)
            throw new IpcException($"Function {target}.{function} takes {d.Parameters.Count} arguments, {args.Count} given");
        var converted = new object?[args.Count];
        for (var i = 0; i < args.Count; i++)
            converted[i] = Parse(args[i], d.Parameters[i].Type!, d.Parameters[i].Name);
        var engine = handler.Engine!;
        var result = engine.Js.CallFunction(handler.GetFunction(function)!, engine.Js.Wrap(handler), converted);
        return Format(result, d.ReturnType!);
    }

    public string GetProperty(string target, string property)
    {
        if (!_handlers.TryGetValue(target, out var handler))
            throw new IpcException($"Target not found: {target}");
        if (handler.GetDynamicProperty(property) is not { } p || !IpcTypes.Contains(p.QmlType))
            throw new IpcException($"Property not found: {target}.{property}");
        return Format(handler.Engine!.Js.ToJs(p.Read()), p.QmlType);
    }

    /// <summary>Waits for a handler signal, calling back with its argument as text each time it fires.</summary>
    public IDisposable Listen(string target, string signal, Action<string> onSignal)
    {
        if (!_handlers.TryGetValue(target, out var handler))
            throw new IpcException($"Target not found: {target}");
        var slot = handler.GetSignal(signal) ?? throw new IpcException($"Signal not found: {target}.{signal}");
        return slot.Connect(args => onSignal(args.Length == 0 ? "" : ValueConverter.ToQmlString(args[0])));
    }

    private static object? Parse(string text, string type, string name) => type switch
    {
        "string" => text,
        "int" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : throw new IpcException($"Argument {name} is not an int: {text}"),
        "real" or "double" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : throw new IpcException($"Argument {name} is not a real: {text}"),
        "bool" => text switch
        {
            "true" => true,
            "false" => false,
            _ when int.TryParse(text, out var n) => n != 0,
            _ => throw new IpcException($"Argument {name} is not a bool: {text}"),
        },
        "color" => QColor.TryParse(text.StartsWith('#') || !IsHex(text) ? text : "#" + text, out var c) ? c : throw new IpcException($"Argument {name} is not a color: {text}"),
        _ => text,
    };

    private static bool IsHex(string s) => s.Length is 3 or 6 or 8 && s.All(char.IsAsciiHexDigit);

    private static string Format(JsValue value, string type)
    {
        var clr = ValueConverter.FromJs(value);
        return type switch
        {
            "void" => "",
            "bool" => ValueConverter.ToBool(clr) ? "true" : "false",
            "color" => ValueConverter.ToColor(clr) is var c ? $"#{c.A8:x2}{c.R8:x2}{c.G8:x2}{c.B8:x2}" : "",
            "int" => ValueConverter.ToInt(clr, "int").ToString(CultureInfo.InvariantCulture),
            _ => ValueConverter.ToQmlString(clr),
        };
    }
}

public sealed class IpcException(string message) : Exception(message);

/// <summary>
/// The <c>Socket</c>: a stream to a Unix domain socket path, or on Windows also a named pipe
/// (<c>\\.\pipe\name</c>).
/// </summary>
[QmlSignal("error", "error")]
public sealed class SocketObject : DataStream
{
    private string _path = "";
    private bool _connected;
    private Stream? _stream;
    private int _generation;
    private readonly StringBuilder _pending = new();

    [QmlEnum]
    public enum SocketError
    {
        ConnectionRefused = 0,
        RemoteHostClosed = 1,
        NotFound = 2,
        Unknown = -1,
    }

    [Qml]
    public string Path
    {
        get { Track(); return _path; }
        set
        {
            if (SetField(ref _path, value ?? "") && _connected)
            {
                Disconnect();
                Connect();
            }
        }
    }

    [Qml]
    public bool Connected
    {
        get { Track(); return _connected; }
        set
        {
            if (value == _connected)
                return;
            if (value)
                Connect();
            else
                Disconnect();
        }
    }

    internal void Adopt(Stream stream)
    {
        _stream = stream;
        _connected = true;
        NotifyChanged("connected");
        StartReading(++_generation);
    }

    private void Connect()
    {
        if (_path.Length == 0 || Engine is null)
            return;
        var generation = ++_generation;
        var loop = Engine.Loop;
        var path = _path;
        Task.Run(async () =>
        {
            try
            {
                Stream stream;
                if (path.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
                {
                    var pipe = new System.IO.Pipes.NamedPipeClientStream(".", path[9..], System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.PipeOptions.Asynchronous);
                    await pipe.ConnectAsync(3000).ConfigureAwait(false);
                    stream = pipe;
                }
                else
                {
                    var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                    await socket.ConnectAsync(new UnixDomainSocketEndPoint(path)).ConfigureAwait(false);
                    stream = new NetworkStream(socket, ownsSocket: true);
                }
                loop.Post(() =>
                {
                    if (generation != _generation || IsDestroyed)
                    {
                        stream.Dispose();
                        return;
                    }
                    _stream = stream;
                    SetField(ref _connected, true, nameof(Connected));
                    if (_pending.Length > 0)
                    {
                        WriteNow(_pending.ToString());
                        _pending.Clear();
                    }
                    StartReading(generation);
                });
            }
            catch (Exception e) when (e is SocketException or IOException or TimeoutException or UnauthorizedAccessException)
            {
                loop.Post(() =>
                {
                    if (generation == _generation && !IsDestroyed)
                        Emit("error", e is TimeoutException or SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressNotAvailable } ? 2 : 0);
                });
            }
        });
    }

    private void StartReading(int generation)
    {
        var stream = _stream!;
        var loop = Engine!.Loop;
        Task.Run(async () =>
        {
            var buffer = new byte[8192];
            var decoder = Encoding.UTF8.GetDecoder();
            var chars = new char[8192];
            try
            {
                while (true)
                {
                    var n = await stream.ReadAsync(buffer).ConfigureAwait(false);
                    if (n <= 0)
                        break;
                    var c = decoder.GetChars(buffer, 0, n, chars, 0);
                    var text = new string(chars, 0, c);
                    loop.Post(() =>
                    {
                        if (generation == _generation && !IsDestroyed)
                            Deliver(text);
                    });
                }
            }
            catch (Exception e) when (e is IOException or ObjectDisposedException or SocketException)
            {
            }
            loop.Post(() =>
            {
                if (generation != _generation || IsDestroyed)
                    return;
                EndStream();
                Disconnect();
            });
        });
    }

    private void Disconnect()
    {
        _generation++;
        _stream?.Dispose();
        _stream = null;
        SetField(ref _connected, false, nameof(Connected));
    }

    [Qml]
    public void Write(string data)
    {
        if (_stream is null)
        {
            _pending.Append(data);
            return;
        }
        WriteNow(data);
    }

    private void WriteNow(string data)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(data);
            _stream!.Write(bytes);
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException)
        {
            Emit("error", 1);
        }
    }

    [Qml]
    public void Flush()
    {
        try
        {
            _stream?.Flush();
        }
        catch (IOException)
        {
        }
    }

    protected override void OnDestroy() => Disconnect();
}

/// <summary>The <c>SocketServer</c>: listens on a path and makes a Socket from its handler for each client.</summary>
public sealed class SocketServer : QObject
{
    private string _path = "";
    private bool _active;
    private QmlComponent? _handler;
    private CancellationTokenSource? _cancel;
    private Task? _listen;
    private int _generation;
    private readonly List<QObject> _clients = [];

    [Qml] public string Path { get { Track(); return _path; } set { if (SetField(ref _path, value ?? "")) Restart(); } }

    [Qml] public bool Active { get { Track(); return _active; } set { if (SetField(ref _active, value)) Restart(); } }

    [Qml] public QmlComponent? Handler { get { Track(); return _handler; } set => SetField(ref _handler, value); }

    public override void ComponentComplete() => Restart();

    private void Restart()
    {
        StopListening();
        if (!IsComplete || !_active || _path.Length == 0 || Engine is null)
            return;
        var cancel = _cancel = new CancellationTokenSource();
        var generation = _generation;
        var loop = Engine.Loop;
        var path = _path;
        var token = cancel.Token;
        // Create the listening instance now; only subsequent I/O needs a background continuation.
        _listen = Listen(path, token, generation, loop);
    }

    private void StopListening()
    {
        var previous = _cancel;
        var listen = _listen;
        _cancel = null;
        _listen = null;
        Interlocked.Increment(ref _generation);
        if (previous is null)
            return;
        previous.Cancel();
        if (listen is null || listen.IsCompleted)
            previous.Dispose();
        else
            listen.ContinueWith(static (_, state) => ((CancellationTokenSource)state!).Dispose(), previous,
                CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    private async Task Listen(string path, CancellationToken token, int generation, IEventLoop loop)
    {
        System.IO.Pipes.NamedPipeServerStream? pipe = null;
        try
        {
            if (path.StartsWith(@"\\.\pipe\", StringComparison.OrdinalIgnoreCase))
            {
                while (!token.IsCancellationRequested)
                {
                    pipe = new System.IO.Pipes.NamedPipeServerStream(path[9..], System.IO.Pipes.PipeDirection.InOut, System.IO.Pipes.NamedPipeServerStream.MaxAllowedServerInstances, System.IO.Pipes.PipeTransmissionMode.Byte, System.IO.Pipes.PipeOptions.Asynchronous);
                    await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                    var connected = pipe;
                    pipe = null;
                    var seen = generation;
                    loop.Post(() =>
                    {
                        if (seen != _generation || IsDestroyed)
                        {
                            connected.Dispose();
                            return;
                        }
                        Accept(connected);
                    });
                }
                return;
            }
            if (File.Exists(path))
                File.Delete(path);
            using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(path));
            listener.Listen(16);
            while (!token.IsCancellationRequested)
            {
                var client = await listener.AcceptAsync(token).ConfigureAwait(false);
                var seen = generation;
                loop.Post(() =>
                {
                    if (seen != _generation || IsDestroyed)
                    {
                        client.Dispose();
                        return;
                    }
                    Accept(new NetworkStream(client, ownsSocket: true));
                });
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception e) when (e is SocketException or IOException or UnauthorizedAccessException)
        {
            loop.Post(() => Engine?.Log.Warn("io", $"SocketServer: cannot listen on {path}: {e.Message}"));
        }
        finally
        {
            pipe?.Dispose();
        }
    }

    private void Accept(Stream stream)
    {
        if (IsDestroyed || _handler is null)
        {
            stream.Dispose();
            return;
        }
        var obj = _handler.Create(null, null, null, o => o.SetOwner(this));
        if (obj is SocketObject socket)
            socket.Adopt(stream);
        else
            stream.Dispose();
        _clients.Add(obj);
    }

    protected override void OnDestroy()
    {
        StopListening();
        if (_path.Length > 0 && !_path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            try
            {
                File.Delete(_path);
            }
            catch (IOException)
            {
            }
        }
    }
}
