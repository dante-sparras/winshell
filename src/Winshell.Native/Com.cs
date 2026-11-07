using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com;

namespace Winshell.Native;

/// <summary>
/// Creates classic COM objects and wraps raw interface pointers as runtime-callable wrappers. Whoever receives a
/// wrapper releases it with <see cref="Release"/>, once for each time it was handed one.
/// </summary>
internal static unsafe class Com
{
    /// <summary>
    /// A new object of class <paramref name="clsid"/> as <typeparamref name="T"/>, or null when Windows cannot create it
    /// or it does not implement <typeparamref name="T"/>. <paramref name="hr"/> is CoCreateInstance's answer:
    /// <c>E_NOINTERFACE</c> for a class without the interface.
    /// </summary>
    internal static T? Create<T>(Guid clsid, CLSCTX context, out int hr) where T : class
    {
        var iid = typeof(T).GUID;
        void* created = null;
        hr = PInvoke.CoCreateInstance(&clsid, null, context, &iid, &created).Value;
        return hr < 0 ? null : Wrap<T>((nint)created);
    }

    /// <summary>
    /// <paramref name="pointer"/>, which holds one reference the caller owns, as <typeparamref name="T"/>. Releases that
    /// reference whatever happens. Null for a null pointer or an object that does not implement
    /// <typeparamref name="T"/>.
    /// </summary>
    internal static T? Wrap<T>(nint pointer) where T : class
    {
        if (pointer == 0)
            return null;
        try
        {
            var wrapper = Marshal.GetObjectForIUnknown(pointer);
            if (wrapper is T wrapped)
                return wrapped;
            Release(wrapper);
            return null;
        }
        finally
        {
            Marshal.Release(pointer);
        }
    }

    /// <summary>
    /// Gives back one of the references a wrapper holds, now rather than when the GC finds it. The runtime hands out
    /// one wrapper per object and counts each hand-out, so another holder keeps its own. Does nothing for null or a
    /// managed object.
    /// </summary>
    internal static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
            Marshal.ReleaseComObject(com);
    }
}
