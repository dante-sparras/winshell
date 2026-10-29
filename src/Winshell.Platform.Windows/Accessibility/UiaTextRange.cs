using System.Runtime.InteropServices;

namespace Winshell.Platform.Windows.Accessibility;

/// <summary>
/// A span of a text field's text for UI Automation's Text pattern. It reads the field through its public model
/// (text, selection, and the position of each character), so it does not depend on how the text is drawn.
/// </summary>
[ComVisible(true)]
internal sealed class UiaTextRange(UiaElement owner, int start, int end) : ITextRangeProvider
{
    private UiaElement Owner => owner;

    private int _start = Math.Min(start, end);
    private int _end = Math.Max(start, end);

    private Runtime.Quick.TextInput Input => owner.Node.TextInput!;

    private string Text => Input.DisplayText;

    /// <summary>Pulls the endpoints back into the text. The field may have shrunk since the range was made.</summary>
    private void Clamp()
    {
        var length = Text.Length;
        _start = Math.Clamp(_start, 0, length);
        _end = Math.Clamp(_end, _start, length);
    }

    private int Run(Action action) => owner.IsAvailable ? owner.Automation.Run(() =>
    {
        if (!owner.IsAvailable)
            return Uia.ElementNotAvailable;
        Clamp();
        action();
        return Uia.S_OK;
    }) : Uia.ElementNotAvailable;

    private int Get<T>(Func<T> read, out T value)
    {
        T result = default!;
        var hr = Run(() => result = read());
        value = result;
        return hr;
    }

    private int Endpoint(int endpoint) => endpoint == Uia.EndpointStart ? _start : _end;

    private void SetEndpoint(int endpoint, int position)
    {
        if (endpoint == Uia.EndpointStart)
        {
            _start = position;
            _end = Math.Max(_end, position);
        }
        else
        {
            _end = position;
            _start = Math.Min(_start, position);
        }
    }

    int ITextRangeProvider.Clone(out ITextRangeProvider? clone)
    {
        ITextRangeProvider? created = null;
        var hr = Run(() => created = new UiaTextRange(owner, _start, _end));
        clone = created;
        return hr;
    }

    int ITextRangeProvider.Compare(ITextRangeProvider range, out int same)
    {
        var match = 0;
        var hr = Run(() =>
        {
            if (range is UiaTextRange other && other.Owner.IsAvailable && other.Owner == owner)
            {
                other.Clamp();
                match = other._start == _start && other._end == _end ? 1 : 0;
            }
        });
        same = match;
        return hr;
    }

    int ITextRangeProvider.CompareEndpoints(int endpoint, ITextRangeProvider target, int targetEndpoint, out int compare)
    {
        compare = 0;
        if (target is not UiaTextRange other)
            return Uia.E_INVALIDARG;
        var delta = 0;
        var code = Uia.S_OK;
        var hr = Run(() =>
        {
            if (!other.Owner.IsAvailable)
                code = Uia.ElementNotAvailable;
            else if (other.Owner != owner)
                code = Uia.E_INVALIDARG;
            else
            {
                other.Clamp();
                delta = Endpoint(endpoint) - other.Endpoint(targetEndpoint);
            }
        });
        if (hr != Uia.S_OK)
            return hr;
        compare = code == Uia.S_OK ? delta : 0;
        return code;
    }

    int ITextRangeProvider.ExpandToEnclosingUnit(int unit) => Run(() => (_start, _end) = TextUnits.Enclose(Text, _start, unit));

    int ITextRangeProvider.FindAttribute(int attribute, object value, int backward, out ITextRangeProvider? found)
    {
        found = null;
        return Uia.S_OK;
    }

    int ITextRangeProvider.FindText(string text, int backward, int ignoreCase, out ITextRangeProvider? found) => Get(() =>
    {
        var span = Text[_start.._end];
        var comparison = ignoreCase != 0 ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture;
        var at = backward != 0 ? span.LastIndexOf(text, comparison) : span.IndexOf(text, comparison);
        return at < 0 || text.Length == 0 ? null : (ITextRangeProvider)new UiaTextRange(owner, _start + at, _start + at + text.Length);
    }, out found);

    int ITextRangeProvider.GetAttributeValue(int attribute, out object? value) =>
        Get(() => attribute == Uia.IsReadOnlyAttribute ? Input.ReadOnly : Uia.NotSupported, out value);

    int ITextRangeProvider.GetBoundingRectangles(out double[]? rectangles) => Get(() =>
    {
        // One line: from the left edge of the first character to the right edge of the last, or the caret.
        var input = Input;
        var a = input.PositionToRectangle(_start);
        var b = input.PositionToRectangle(_end);
        var left = Math.Max(0, a.X);
        var right = Math.Min(input.Width, Math.Max(left, b.X));
        var origin = input.MapToScene(left, a.Y);
        var rect = owner.Automation.ToScreen(new Runtime.QRect(origin.X, origin.Y, right - left, a.Height));
        return new[] { rect.Left, rect.Top, rect.Width, rect.Height };
    }, out rectangles);

    int ITextRangeProvider.GetEnclosingElement(out IRawElementProviderSimple? element)
    {
        element = owner;
        return Uia.S_OK;
    }

    int ITextRangeProvider.GetText(int maxLength, out string text) => Get(() =>
    {
        var span = Text[_start.._end];
        return maxLength >= 0 && span.Length > maxLength ? span[..maxLength] : span;
    }, out text);

    int ITextRangeProvider.Move(int unit, int count, out int moved) => Get(() =>
    {
        (_start, _end, var steps) = TextUnits.Move(Text, _start, _end, unit, count);
        return steps;
    }, out moved);

    int ITextRangeProvider.MoveEndpointByUnit(int endpoint, int unit, int count, out int moved) => Get(() =>
    {
        var (position, steps) = TextUnits.MoveEndpoint(Text, Endpoint(endpoint), unit, count);
        SetEndpoint(endpoint, position);
        return steps;
    }, out moved);

    int ITextRangeProvider.MoveEndpointByRange(int endpoint, ITextRangeProvider target, int targetEndpoint)
    {
        if (target is not UiaTextRange other || other.Owner != owner)
            return Uia.E_INVALIDARG;
        return Run(() => SetEndpoint(endpoint, other.Endpoint(targetEndpoint)));
    }

    int ITextRangeProvider.Select() => Run(() => Input.Select(_start, _end));

    int ITextRangeProvider.AddToSelection() => Uia.InvalidOperation;

    int ITextRangeProvider.RemoveFromSelection() => Uia.InvalidOperation;

    int ITextRangeProvider.ScrollIntoView(int alignToTop) => Run(() => Input.EnsureVisible(alignToTop != 0 ? _start : _end));

    int ITextRangeProvider.GetChildren(out object[]? children)
    {
        children = [];
        return Uia.S_OK;
    }
}
