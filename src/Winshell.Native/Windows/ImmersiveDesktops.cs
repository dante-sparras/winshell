using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.System.Com;
using Windows.Win32.System.WinRT;

namespace Winshell.Native;

/// <summary>A virtual desktop: its id, and the name the user gave it or empty.</summary>
internal readonly record struct VirtualDesktopEntry(Guid Id, string Name);

/// <summary>
/// The virtual desktops through Explorer's internal manager, reached through the immersive shell. Its method table
/// changes between Windows builds, so the caller checks the build first. Each call takes the manager fresh and
/// releases every object before it returns. A failed step goes no further and says what failed.
/// </summary>
internal static class ImmersiveDesktops
{
    private static readonly Guid ImmersiveShellClass = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid InternalService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");

    /// <summary>More desktops than this is a broken answer, not a list to read.</summary>
    private const uint MaxDesktops = 128;

    /// <summary>
    /// Every desktop in order, and the current one. <paramref name="error"/> starts with <c>unavailable:</c> when the
    /// manager cannot be reached, and with <c>failed:</c> when it gave a broken answer.
    /// </summary>
    internal static bool TryRead(out IReadOnlyList<VirtualDesktopEntry> desktops, out Guid current, out string error)
    {
        desktops = [];
        current = Guid.Empty;
        if (!TryInternal(out var manager, out error))
            return false;

        List<IVirtualDesktop>? listed = null;
        IVirtualDesktop? currentDesktop = null;
        try
        {
            var hr = manager.GetCount(out var count);
            if (hr < 0)
            {
                error = $"failed: GetCount {Hex(hr)}";
                return false;
            }

            if (count is 0 or > MaxDesktops)
            {
                error = $"failed: GetCount returned {count}";
                return false;
            }

            listed = Desktops(manager, out error);
            if (listed is null)
                return false;
            if (listed.Count != count)
            {
                error = $"failed: the desktop list has {listed.Count} entries after GetCount returned {count}";
                return false;
            }

            var entries = new VirtualDesktopEntry[count];
            for (var i = 0; i < listed.Count; i++)
            {
                hr = listed[i].GetId(out var id);
                if (hr < 0 || id == Guid.Empty)
                {
                    error = $"failed: desktop {i} has no id ({Hex(hr)})";
                    return false;
                }

                entries[i] = new VirtualDesktopEntry(id, ReadName(listed[i]));
            }

            hr = manager.GetCurrentDesktop(out var currentPointer);
            currentDesktop = Com.Wrap<IVirtualDesktop>(currentPointer);
            if (hr < 0 || currentDesktop is null)
            {
                error = $"failed: GetCurrentDesktop {Hex(hr)}";
                return false;
            }

            hr = currentDesktop.GetId(out var currentId);
            if (hr < 0 || currentId == Guid.Empty)
            {
                error = $"failed: the current desktop has no id ({Hex(hr)})";
                return false;
            }

            desktops = entries;
            current = currentId;
            error = "";
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            error = $"failed: {ex.GetType().Name}";
            return false;
        }
        finally
        {
            Release(listed);
            Com.Release(currentDesktop);
            Com.Release(manager);
        }
    }

    /// <summary>Switches to the desktop with that id. <paramref name="problem"/> says why not, after the method's name.</summary>
    internal static bool SwitchTo(Guid id, out string problem)
    {
        if (!TryInternal(out var manager, out var error))
        {
            problem = $"did not run ({error})";
            return false;
        }

        nint pointer = 0;
        try
        {
            if (!TryFind(manager, id, out pointer))
            {
                problem = "could not find that desktop";
                return false;
            }

            var hr = manager.SwitchDesktop(pointer);
            problem = hr < 0 ? $"failed ({Hex(hr)})" : "";
            return hr >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            problem = $"failed ({ex.GetType().Name})";
            return false;
        }
        finally
        {
            if (pointer != 0)
                Marshal.Release(pointer);
            Com.Release(manager);
        }
    }

    /// <summary>Adds a desktop and returns its id, or null. <paramref name="problem"/> says why not, after the method's name.</summary>
    internal static Guid? Create(out string problem)
    {
        if (!TryInternal(out var manager, out var error))
        {
            problem = $"did not run ({error})";
            return null;
        }

        IVirtualDesktop? created = null;
        try
        {
            var hr = manager.CreateDesktop(out var pointer);
            created = Com.Wrap<IVirtualDesktop>(pointer);
            if (hr < 0 || created is null)
            {
                problem = $"failed ({Hex(hr)})";
                return null;
            }

            hr = created.GetId(out var id);
            if (hr < 0 || id == Guid.Empty)
            {
                problem = $"returned a desktop with no id ({Hex(hr)})";
                return null;
            }

            problem = "";
            return id;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            problem = $"failed ({ex.GetType().Name})";
            return null;
        }
        finally
        {
            Com.Release(created);
            Com.Release(manager);
        }
    }

    /// <summary>
    /// Removes the desktop <paramref name="id"/>, moving its windows to <paramref name="fallback"/>.
    /// <paramref name="problem"/> says why not, after the method's name.
    /// </summary>
    internal static bool Remove(Guid id, Guid fallback, out string problem)
    {
        if (!TryInternal(out var manager, out var error))
        {
            problem = $"did not run ({error})";
            return false;
        }

        nint destroy = 0, onto = 0;
        try
        {
            if (!TryFind(manager, id, out destroy) || !TryFind(manager, fallback, out onto))
            {
                problem = "could not find the desktop or its fallback";
                return false;
            }

            var hr = manager.RemoveDesktop(destroy, onto);
            problem = hr < 0 ? $"failed ({Hex(hr)})" : "";
            return hr >= 0;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            problem = $"failed ({ex.GetType().Name})";
            return false;
        }
        finally
        {
            if (destroy != 0)
                Marshal.Release(destroy);
            if (onto != 0)
                Marshal.Release(onto);
            Com.Release(manager);
        }
    }

    private static bool TryInternal([NotNullWhen(true)] out IVirtualDesktopManagerInternal? manager,
        out string error)
    {
        manager = null;
        var shell = Com.Create<IServiceProvider>(ImmersiveShellClass, CLSCTX.CLSCTX_LOCAL_SERVER, out var hr);
        if (shell is null)
        {
            error = $"unavailable: immersive shell {Hex(hr)}";
            return false;
        }

        try
        {
            var iid = typeof(IVirtualDesktopManagerInternal).GUID;
            var service = InternalService;
            hr = shell.QueryService(in service, in iid, out var managerPointer);
            manager = Com.Wrap<IVirtualDesktopManagerInternal>(managerPointer);
            if (hr < 0 || manager is null)
            {
                Com.Release(manager);
                manager = null;
                error = $"unavailable: virtual desktop service {Hex(hr)}";
                return false;
            }

            error = "";
            return true;
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or InvalidComObjectException)
        {
            error = $"unavailable: {ex.GetType().Name}";
            manager = null;
            return false;
        }
        finally
        {
            Com.Release(shell);
        }
    }

    /// <summary>The desktop with that id, as an <c>IVirtualDesktop</c> pointer with one reference the caller releases.</summary>
    private static bool TryFind(IVirtualDesktopManagerInternal manager, Guid id, out nint pointer)
    {
        pointer = 0;
        var desktops = Desktops(manager, out _);
        if (desktops is null)
            return false;
        try
        {
            foreach (var desktop in desktops)
            {
                if (desktop.GetId(out var found) < 0 || found != id)
                    continue;
                pointer = Marshal.GetComInterfaceForObject(desktop, typeof(IVirtualDesktop));
                return true;
            }

            return false;
        }
        finally
        {
            Release(desktops);
        }
    }

    /// <summary>
    /// Every desktop in the manager's list, in order, for the caller to <see cref="Release(List{IVirtualDesktop}?)"/>.
    /// Null when the list cannot be read, and <paramref name="error"/> says why.
    /// </summary>
    private static List<IVirtualDesktop>? Desktops(IVirtualDesktopManagerInternal manager, out string error)
    {
        var hr = manager.GetDesktops(out var arrayPointer);
        var array = Com.Wrap<IObjectArray>(arrayPointer);
        if (hr < 0 || array is null)
        {
            Com.Release(array);
            error = $"failed: GetDesktops {Hex(hr)}";
            return null;
        }

        var desktops = new List<IVirtualDesktop>();
        var complete = false;
        try
        {
            hr = array.GetCount(out var count);
            if (hr < 0 || count > MaxDesktops)
            {
                error = $"failed: the desktop list has {count} entries ({Hex(hr)})";
                return null;
            }

            var iid = typeof(IVirtualDesktop).GUID;
            for (uint i = 0; i < count; i++)
            {
                hr = array.GetAt(i, in iid, out var item);
                if (hr < 0 || item is not IVirtualDesktop desktop)
                {
                    Com.Release(item);
                    error = $"failed: GetAt {i} {Hex(hr)}";
                    return null;
                }

                desktops.Add(desktop);
            }

            error = "";
            complete = true;
            return desktops;
        }
        finally
        {
            if (!complete)
                Release(desktops);
            Com.Release(array);
        }
    }

    private static void Release(List<IVirtualDesktop>? desktops)
    {
        foreach (var desktop in desktops ?? [])
            Com.Release(desktop);
    }

    private static unsafe string ReadName(IVirtualDesktop desktop)
    {
        if (desktop.GetName(out var name) < 0 || name == 0)
            return "";
        try
        {
            var text = PInvoke.WindowsGetStringRawBuffer(new HSTRING((void*)name), out var length);
            return text.Value is null ? "" : new string(text.Value, 0, (int)length);
        }
        finally
        {
            _ = PInvoke.WindowsDeleteString(new HSTRING((void*)name));
        }
    }

    private static string Hex(int hr)
    {
        return $"0x{hr:X8}";
    }
}
