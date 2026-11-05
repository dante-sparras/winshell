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
internal static unsafe class ImmersiveDesktops
{
    private static readonly Guid ImmersiveShellClass = new("C2F03A33-21F5-47FA-B4BB-156362A2F239");
    private static readonly Guid InternalService = new("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B");
    private static readonly Guid ServiceProviderId = new("6D5140C1-7436-11CE-8034-00AA006009FA");
    private static readonly Guid ManagerId = new("53F5CA0B-158F-4124-900C-057158060B27");
    private static readonly Guid DesktopId = new("3F07F4BE-B107-441A-AF0F-39D82529072C");

    /// <summary>More desktops than this is a broken answer, not a list to read.</summary>
    private const uint MaxDesktops = 128;

    /// <summary>
    /// <c>IVirtualDesktopManagerInternal</c>'s slots on builds 26100 through 26399, after IUnknown's three. Slots
    /// Windows inserted in 24H2 stay, including <c>MoveDesktop</c>, which is not called: removing it would move
    /// <c>RemoveDesktop</c> onto the wrong slot.
    /// </summary>
    private enum Manager
    {
        GetCount = 3,
        MoveViewToDesktop,
        CanViewMoveDesktops,
        GetCurrentDesktop,
        GetDesktops,
        GetAdjacentDesktop,
        SwitchDesktop,
        SwitchDesktopAndMoveForegroundView,
        CreateDesktop,
        MoveDesktop,
        RemoveDesktop,
    }

    /// <summary><c>IVirtualDesktop</c>'s slots, after IUnknown's three.</summary>
    private enum Desktop
    {
        IsViewVisible = 3,
        GetId,
        GetName,
    }

    /// <summary><c>IObjectArray</c>'s slots, after IUnknown's three.</summary>
    private enum ObjectArray
    {
        GetCount = 3,
        GetAt,
    }

    // IServiceProvider's one method, after IUnknown's three.
    private const int QueryServiceSlot = 3;

    /// <summary>
    /// Every desktop in order, and the current one. <paramref name="error"/> starts with <c>unavailable:</c> when the
    /// manager cannot be reached, and with <c>failed:</c> when it gave a broken answer.
    /// </summary>
    internal static bool TryRead(out IReadOnlyList<VirtualDesktopEntry> desktops, out Guid current, out string error)
    {
        desktops = [];
        current = Guid.Empty;
        if (!TryManager(out var manager, out error))
            return false;
        void* array = null;
        try
        {
            uint count;
            var hr = ComSlot.Call(manager, (int)Manager.GetCount, &count);
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

            hr = ComSlot.Call(manager, (int)Manager.GetDesktops, &array);
            if (hr < 0 || array is null)
            {
                error = $"failed: GetDesktops {Hex(hr)}";
                return false;
            }
            uint listed = 0;
            hr = ComSlot.Call(array, (int)ObjectArray.GetCount, &listed);
            if (hr < 0 || listed != count)
            {
                error = $"failed: the desktop list has {listed} entries after GetCount returned {count} ({Hex(hr)})";
                return false;
            }

            var iid = DesktopId;
            var entries = new VirtualDesktopEntry[count];
            for (uint i = 0; i < count; i++)
            {
                void* item = null;
                hr = ComSlot.Call(array, (int)ObjectArray.GetAt, i, &iid, &item);
                if (hr < 0 || item is null)
                {
                    error = $"failed: GetAt {i} {Hex(hr)}";
                    return false;
                }
                try
                {
                    Guid id;
                    hr = ComSlot.Call(item, (int)Desktop.GetId, &id);
                    if (hr < 0 || id == Guid.Empty)
                    {
                        error = $"failed: desktop {i} has no id ({Hex(hr)})";
                        return false;
                    }
                    entries[i] = new VirtualDesktopEntry(id, ReadName(item));
                }
                finally
                {
                    ComSlot.Release(item);
                }
            }

            void* currentDesktop = null;
            hr = ComSlot.Call(manager, (int)Manager.GetCurrentDesktop, &currentDesktop);
            if (hr < 0 || currentDesktop is null)
            {
                error = $"failed: GetCurrentDesktop {Hex(hr)}";
                return false;
            }
            Guid currentId;
            try
            {
                hr = ComSlot.Call(currentDesktop, (int)Desktop.GetId, &currentId);
            }
            finally
            {
                ComSlot.Release(currentDesktop);
            }
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
        finally
        {
            ComSlot.Release(array);
            ComSlot.Release(manager);
        }
    }

    /// <summary>Switches to the desktop with that id. <paramref name="problem"/> says why not, after the method's name.</summary>
    internal static bool SwitchTo(Guid id, out string problem)
    {
        if (!TryManager(out var manager, out var error))
        {
            problem = $"did not run ({error})";
            return false;
        }
        void* desktop = null;
        try
        {
            if (!TryFind(manager, id, out desktop))
            {
                problem = "could not find that desktop";
                return false;
            }
            var hr = ComSlot.Call(manager, (int)Manager.SwitchDesktop, desktop);
            problem = hr < 0 ? $"failed ({Hex(hr)})" : "";
            return hr >= 0;
        }
        finally
        {
            ComSlot.Release(desktop);
            ComSlot.Release(manager);
        }
    }

    /// <summary>Adds a desktop and returns its id, or null. <paramref name="problem"/> says why not, after the method's name.</summary>
    internal static Guid? Create(out string problem)
    {
        if (!TryManager(out var manager, out var error))
        {
            problem = $"did not run ({error})";
            return null;
        }
        void* created = null;
        try
        {
            var hr = ComSlot.Call(manager, (int)Manager.CreateDesktop, &created);
            if (hr < 0 || created is null)
            {
                problem = $"failed ({Hex(hr)})";
                return null;
            }
            Guid id;
            hr = ComSlot.Call(created, (int)Desktop.GetId, &id);
            if (hr < 0 || id == Guid.Empty)
            {
                problem = $"returned a desktop with no id ({Hex(hr)})";
                return null;
            }
            problem = "";
            return id;
        }
        finally
        {
            ComSlot.Release(created);
            ComSlot.Release(manager);
        }
    }

    /// <summary>
    /// Removes the desktop <paramref name="id"/>, moving its windows to <paramref name="fallback"/>.
    /// <paramref name="problem"/> says why not, after the method's name.
    /// </summary>
    internal static bool Remove(Guid id, Guid fallback, out string problem)
    {
        if (!TryManager(out var manager, out var error))
        {
            problem = $"did not run ({error})";
            return false;
        }
        void* destroy = null;
        void* onto = null;
        try
        {
            if (!TryFind(manager, id, out destroy) || !TryFind(manager, fallback, out onto))
            {
                problem = "could not find the desktop or its fallback";
                return false;
            }
            var hr = ComSlot.Call(manager, (int)Manager.RemoveDesktop, destroy, onto);
            problem = hr < 0 ? $"failed ({Hex(hr)})" : "";
            return hr >= 0;
        }
        finally
        {
            ComSlot.Release(destroy);
            ComSlot.Release(onto);
            ComSlot.Release(manager);
        }
    }

    /// <summary>The internal manager, with one reference the caller releases.</summary>
    private static bool TryManager(out void* manager, out string error)
    {
        manager = null;
        var hr = Win32.CoCreateInstance(in ImmersiveShellClass, 0, (uint)CLSCTX.CLSCTX_LOCAL_SERVER, in ServiceProviderId, out var shell);
        if (hr < 0 || shell == 0)
        {
            error = $"unavailable: immersive shell {Hex(hr)}";
            return false;
        }
        try
        {
            var service = InternalService;
            var iid = ManagerId;
            void* found = null;
            hr = ComSlot.Call((void*)shell, QueryServiceSlot, &service, &iid, &found);
            if (hr < 0 || found is null)
            {
                error = $"unavailable: virtual desktop service {Hex(hr)}";
                return false;
            }
            manager = found;
            error = "";
            return true;
        }
        finally
        {
            Marshal.Release(shell);
        }
    }

    /// <summary>The desktop with that id, with one reference the caller releases.</summary>
    private static bool TryFind(void* manager, Guid id, out void* desktop)
    {
        desktop = null;
        void* array = null;
        var hr = ComSlot.Call(manager, (int)Manager.GetDesktops, &array);
        if (hr < 0 || array is null)
            return false;
        try
        {
            uint count;
            if (ComSlot.Call(array, (int)ObjectArray.GetCount, &count) < 0 || count > MaxDesktops)
                return false;
            var iid = DesktopId;
            for (uint i = 0; i < count; i++)
            {
                void* item = null;
                if (ComSlot.Call(array, (int)ObjectArray.GetAt, i, &iid, &item) < 0 || item is null)
                    return false;
                Guid found;
                if (ComSlot.Call(item, (int)Desktop.GetId, &found) >= 0 && found == id)
                {
                    desktop = item;
                    return true;
                }
                ComSlot.Release(item);
            }
            return false;
        }
        finally
        {
            ComSlot.Release(array);
        }
    }

    private static string ReadName(void* desktop)
    {
        nint name = 0;
        if (ComSlot.Call(desktop, (int)Desktop.GetName, &name) < 0 || name == 0)
            return "";
        try
        {
            var text = PInvoke.WindowsGetStringRawBuffer(new HSTRING((void*)name), out var length);
            return text.Value is null ? "" : new string(text.Value, 0, (int)length);
        }
        finally
        {
            _ = Win32.WindowsDeleteString(name);
        }
    }

    private static string Hex(int hr) => $"0x{hr:X8}";
}
