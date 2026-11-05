using System.Runtime.InteropServices;

namespace Winshell.Native;

/// <summary>
/// Calls a COM method by its vtable slot and returns its HRESULT. Two of the virtual desktop interfaces are
/// undocumented, and CsWin32's form of the documented ones throws on a failed HRESULT, which these callers report
/// instead. Every pointer argument passes as <c>void*</c>.
/// </summary>
internal static unsafe class ComSlot
{
    internal static int Call(void* com, int slot, void* a) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, int>)Method(com, slot))(com, a);

    internal static int Call(void* com, int slot, void* a, void* b) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, int>)Method(com, slot))(com, a, b);

    internal static int Call(void* com, int slot, void* a, void* b, void* c) =>
        ((delegate* unmanaged[Stdcall]<void*, void*, void*, void*, int>)Method(com, slot))(com, a, b, c);

    internal static int Call(void* com, int slot, uint a, void* b, void* c) =>
        ((delegate* unmanaged[Stdcall]<void*, uint, void*, void*, int>)Method(com, slot))(com, a, b, c);

    internal static int Call(void* com, int slot, nint a, void* b) =>
        ((delegate* unmanaged[Stdcall]<void*, nint, void*, int>)Method(com, slot))(com, a, b);

    /// <summary>Releases one reference. Null does nothing.</summary>
    internal static void Release(void* com)
    {
        if (com is not null)
            Marshal.Release((nint)com);
    }

    private static void* Method(void* com, int slot) => (*(void***)com)[slot];
}
