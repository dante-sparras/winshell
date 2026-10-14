using System.Text.Json;

namespace Winshell.Core;

/// <summary>
/// A plugin whose manifest is the widget. Saving the file updates the face within five seconds, and the open panel on the next turn.
/// The host draws the panel from <see cref="Sheet"/> with the Kit, and builds it again when <see cref="Revision"/> moves.
/// </summary>
public class SheetPlugin : BarPlugin
{
    private readonly string _path;
    private readonly string _name;
    private PluginSheet _sheet;
    private DateTime _written;
    private string _error = "";
    private bool _published;
    private bool _read;

    /// <summary>What a missing file reports as its write time.</summary>
    private static readonly DateTime Missing = DateTime.FromFileTimeUtc(0);

    /// <summary>Creates the plugin the manifest named.</summary>
    /// <param name="manifest">A manifest with a sheet.</param>
    public SheetPlugin(PluginManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        Id = manifest.Id;
        _name = manifest.Name;
        _path = manifest.Path;
        _sheet = manifest.Sheet ?? new PluginSheet(manifest.Name, "", manifest.Name, false, manifest.Name, []);
        _read = true;
        _written = Written();
    }

    /// <inheritdoc />
    public override string Id { get; }

    /// <summary>The sheet as last read from the manifest.</summary>
    public PluginSheet Sheet => _sheet;

    /// <summary>Moves each time the manifest is read again into a new sheet.</summary>
    public long Revision { get; private set; }

    /// <inheritdoc />
    /// <remarks>
    /// A tick reads the manifest's write time from the disk, so the item asks for one every five seconds.
    /// An open panel keeps that wait. A save shows on the next tick that is due.
    /// </remarks>
    public override TimeSpan NextTick() => TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    public override void Tick()
    {
        var error = _error;
        var read = Refresh();
        if (read)
        {
            Revision++;
        }

        // The face is made of the sheet and the error, so it is built again only when one of them moved.
        if (_published && !read && string.Equals(error, _error, StringComparison.Ordinal))
        {
            return;
        }

        _published = true;
        Publish(new BarFace
        {
            Text = _sheet.Text,
            Glyph = _sheet.Glyph,
            Label = _error.Length > 0 ? _error : _sheet.Text,
            Tooltip = _error.Length > 0 ? _error : _sheet.Tooltip,
            Accent = _error.Length > 0 || _sheet.Accent,
        });
    }

    /// <inheritdoc />
    public override void OnGesture(WidgetGesture gesture, int tag)
    {
        if (gesture == WidgetGesture.Left)
        {
            Context.OpenPanel();
        }
    }

    /// <inheritdoc />
    public override void OnPanel(string command, double? value)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.Equals(PluginSheet.Close, StringComparison.OrdinalIgnoreCase))
        {
            Context.DismissPanel();
        }
    }

    private bool Refresh()
    {
        if (_path.Length == 0)
        {
            return false;
        }

        // One read of the file's attributes. A missing file answers with the first moment a write time can hold, and is left as it was shown.
        DateTime written;
        try
        {
            written = File.GetLastWriteTimeUtc(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        if (written == Missing || (_read && written == _written))
        {
            return false;
        }

        string? text;
        try
        {
            text = PluginManifest.ReadText(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The editor still holds the file. The write time is not taken as read, so the next tick reads again.
            _error = Id + " sheet could not be read";
            return false;
        }

        _written = written;
        _read = true;
        if (text is null)
        {
            _error = Id + " manifest is too large";
            return false;
        }

        try
        {
            // A save that is not an object, such as [], has no properties to ask for.
            using var document = JsonDocument.Parse(text);
            if (document.RootElement.ValueKind != JsonValueKind.Object || !document.RootElement.TryGetProperty("sheet", out var sheet))
            {
                _error = Id + " is missing its sheet";
                return false;
            }

            if (!PluginSheet.TryRead(sheet, Id, _name, out var parsed, out var error) || parsed is null)
            {
                _error = error ?? Id + " sheet could not be read";
                return false;
            }

            _sheet = parsed;
            _error = "";
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            // A string that holds half a surrogate pair parses and then throws InvalidOperationException when it is read.
            _error = Id + " sheet could not be read";
            return false;
        }
    }

    private DateTime Written()
    {
        try
        {
            return _path.Length == 0 ? Missing : File.GetLastWriteTimeUtc(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Missing;
        }
    }
}
