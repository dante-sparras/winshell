using System.Runtime.InteropServices;

namespace Winshell.Platform.Windows.Accessibility;

// The UI Automation provider interfaces from UIAutomationCore.idl, in their vtable order.

[ComImport, Guid("d6dd68d1-86fd-4332-8666-9abedea2d24c"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderSimple
{
    [PreserveSig] int get_ProviderOptions(out int options);
    [PreserveSig] int GetPatternProvider(int patternId, [MarshalAs(UnmanagedType.IUnknown)] out object? pattern);
    [PreserveSig] int GetPropertyValue(int propertyId, [MarshalAs(UnmanagedType.Struct)] out object? value);
    [PreserveSig] int get_HostRawElementProvider(out IRawElementProviderSimple? host);
}

[ComImport, Guid("f7063da8-8359-439c-9297-bbc5299a7d87"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragment
{
    [PreserveSig] int Navigate(int direction, out IRawElementProviderFragment? element);
    [PreserveSig] int GetRuntimeId([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_I4)] out int[]? runtimeId);
    [PreserveSig] int get_BoundingRectangle(out UiaRect rect);
    [PreserveSig] int GetEmbeddedFragmentRoots([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] out object[]? roots);
    [PreserveSig] int SetFocus();
    [PreserveSig] int get_FragmentRoot(out IRawElementProviderFragmentRoot? root);
}

[ComImport, Guid("620ce2a5-ab8f-40a9-86cb-de3c75599b58"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRawElementProviderFragmentRoot
{
    [PreserveSig] int ElementProviderFromPoint(double x, double y, out IRawElementProviderFragment? element);
    [PreserveSig] int GetFocus(out IRawElementProviderFragment? element);
}

[ComImport, Guid("54fcb24b-e18e-47a2-b4d3-eccbe77599a2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IInvokeProvider
{
    [PreserveSig] int Invoke();
}

[ComImport, Guid("56d00bd0-c4f4-433c-a836-1a52a57e0892"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IToggleProvider
{
    [PreserveSig] int Toggle();
    [PreserveSig] int get_ToggleState(out int state);
}

[ComImport, Guid("36dc7aef-33e6-4691-afe1-2be7274b3d33"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IRangeValueProvider
{
    [PreserveSig] int SetValue(double value);
    [PreserveSig] int get_Value(out double value);
    [PreserveSig] int get_IsReadOnly(out int readOnly);
    [PreserveSig] int get_Maximum(out double maximum);
    [PreserveSig] int get_Minimum(out double minimum);
    [PreserveSig] int get_LargeChange(out double change);
    [PreserveSig] int get_SmallChange(out double change);
}

[ComImport, Guid("c7935180-6fb3-4201-b174-7df73adbf64a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IValueProvider
{
    [PreserveSig] int SetValue([MarshalAs(UnmanagedType.LPWStr)] string value);
    [PreserveSig] int get_Value([MarshalAs(UnmanagedType.BStr)] out string value);
    [PreserveSig] int get_IsReadOnly(out int readOnly);
}

[ComImport, Guid("fb8b03af-3bdf-48d4-bd36-1a65793be168"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISelectionProvider
{
    [PreserveSig] int GetSelection([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] out object[]? selection);
    [PreserveSig] int get_CanSelectMultiple(out int value);
    [PreserveSig] int get_IsSelectionRequired(out int value);
}

[ComImport, Guid("2acad808-b2d4-452d-a407-91ff1ad167b2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ISelectionItemProvider
{
    [PreserveSig] int Select();
    [PreserveSig] int AddToSelection();
    [PreserveSig] int RemoveFromSelection();
    [PreserveSig] int get_IsSelected(out int value);
    [PreserveSig] int get_SelectionContainer(out IRawElementProviderSimple? container);
}

[ComImport, Guid("d847d3a5-cab0-4a98-8c32-ecb45c59ad24"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IExpandCollapseProvider
{
    [PreserveSig] int Expand();
    [PreserveSig] int Collapse();
    [PreserveSig] int get_ExpandCollapseState(out int state);
}

[ComImport, Guid("b38b8077-1fc3-42a5-8cae-d40c2215055a"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IScrollProvider
{
    [PreserveSig] int Scroll(int horizontalAmount, int verticalAmount);
    [PreserveSig] int SetScrollPercent(double horizontalPercent, double verticalPercent);
    [PreserveSig] int get_HorizontalScrollPercent(out double value);
    [PreserveSig] int get_VerticalScrollPercent(out double value);
    [PreserveSig] int get_HorizontalViewSize(out double value);
    [PreserveSig] int get_VerticalViewSize(out double value);
    [PreserveSig] int get_HorizontallyScrollable(out int value);
    [PreserveSig] int get_VerticallyScrollable(out int value);
}

[ComImport, Guid("3589c92c-63f3-4367-99bb-ada653b77cf2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextProvider
{
    [PreserveSig] int GetSelection([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] out object[]? ranges);
    [PreserveSig] int GetVisibleRanges([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] out object[]? ranges);
    [PreserveSig] int RangeFromChild(IRawElementProviderSimple child, out ITextRangeProvider? range);
    [PreserveSig] int RangeFromPoint(UiaPoint point, out ITextRangeProvider? range);
    [PreserveSig] int get_DocumentRange(out ITextRangeProvider? range);
    [PreserveSig] int get_SupportedTextSelection(out int value);
}

[ComImport, Guid("5347ad7b-c355-46f8-aff5-909033582f63"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface ITextRangeProvider
{
    [PreserveSig] int Clone(out ITextRangeProvider? clone);
    [PreserveSig] int Compare(ITextRangeProvider range, out int same);
    [PreserveSig] int CompareEndpoints(int endpoint, ITextRangeProvider target, int targetEndpoint, out int compare);
    [PreserveSig] int ExpandToEnclosingUnit(int unit);
    [PreserveSig] int FindAttribute(int attribute, [MarshalAs(UnmanagedType.Struct)] object value, int backward, out ITextRangeProvider? found);
    [PreserveSig] int FindText([MarshalAs(UnmanagedType.BStr)] string text, int backward, int ignoreCase, out ITextRangeProvider? found);
    [PreserveSig] int GetAttributeValue(int attribute, [MarshalAs(UnmanagedType.Struct)] out object? value);
    [PreserveSig] int GetBoundingRectangles([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_R8)] out double[]? rectangles);
    [PreserveSig] int GetEnclosingElement(out IRawElementProviderSimple? element);
    [PreserveSig] int GetText(int maxLength, [MarshalAs(UnmanagedType.BStr)] out string text);
    [PreserveSig] int Move(int unit, int count, out int moved);
    [PreserveSig] int MoveEndpointByUnit(int endpoint, int unit, int count, out int moved);
    [PreserveSig] int MoveEndpointByRange(int endpoint, ITextRangeProvider target, int targetEndpoint);
    [PreserveSig] int Select();
    [PreserveSig] int AddToSelection();
    [PreserveSig] int RemoveFromSelection();
    [PreserveSig] int ScrollIntoView(int alignToTop);
    [PreserveSig] int GetChildren([MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_UNKNOWN)] out object[]? children);
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaRect
{
    public double Left, Top, Width, Height;
}

[StructLayout(LayoutKind.Sequential)]
internal struct UiaPoint
{
    public double X, Y;
}

/// <summary>The UI Automation ids winshell uses, and the functions of UIAutomationCore.dll.</summary>
internal static partial class Uia
{
    public const int S_OK = 0;
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int E_INVALIDARG = unchecked((int)0x80070057);
    public const int ElementNotEnabled = unchecked((int)0x80040200);
    public const int ElementNotAvailable = unchecked((int)0x80040201);
    public const int InvalidOperation = unchecked((int)0x80131509);

    public const int RootObjectId = -25;
    public const int AppendRuntimeId = 3;

    public const int ServerSideProvider = 0x2;
    public const int UseComThreading = 0x20;

    // Patterns
    public const int InvokePattern = 10000;
    public const int SelectionPattern = 10001;
    public const int ValuePattern = 10002;
    public const int RangeValuePattern = 10003;
    public const int ScrollPattern = 10004;
    public const int ExpandCollapsePattern = 10005;
    public const int SelectionItemPattern = 10010;
    public const int TextPattern = 10014;
    public const int TogglePattern = 10015;

    // Properties
    public const int ControlTypeProperty = 30003;
    public const int LocalizedControlTypeProperty = 30004;
    public const int NameProperty = 30005;
    public const int HasKeyboardFocusProperty = 30008;
    public const int IsKeyboardFocusableProperty = 30009;
    public const int IsEnabledProperty = 30010;
    public const int AutomationIdProperty = 30011;
    public const int ClassNameProperty = 30012;
    public const int HelpTextProperty = 30013;
    public const int IsControlElementProperty = 30016;
    public const int IsContentElementProperty = 30017;
    public const int IsPasswordProperty = 30019;
    public const int IsOffscreenProperty = 30022;
    public const int FrameworkIdProperty = 30024;
    public const int ValueValueProperty = 30045;
    public const int RangeValueValueProperty = 30047;
    public const int HorizontalScrollPercentProperty = 30053;
    public const int VerticalScrollPercentProperty = 30055;
    public const int ExpandCollapseStateProperty = 30070;
    public const int IsSelectedProperty = 30079;
    public const int ToggleStateProperty = 30086;

    // Events
    public const int StructureChangedEvent = 20002;
    public const int MenuOpenedEvent = 20003;
    public const int FocusChangedEvent = 20005;
    public const int MenuClosedEvent = 20007;
    public const int InvokedEvent = 20009;
    public const int ElementSelectedEvent = 20012;
    public const int TextSelectionChangedEvent = 20014;
    public const int TextChangedEvent = 20015;

    public const int ChildrenInvalidated = 2;

    // Control types
    public const int Button = 50000, CheckBox = 50002, ComboBox = 50003, Edit = 50004, Hyperlink = 50005, Image = 50006,
        ListItem = 50007, List = 50008, Menu = 50009, MenuBar = 50010, MenuItem = 50011, ProgressBar = 50012,
        RadioButton = 50013, ScrollBar = 50014, Slider = 50015, Spinner = 50016, StatusBar = 50017, Tab = 50018,
        TabItem = 50019, Text = 50020, ToolBar = 50021, ToolTip = 50022, Tree = 50023, TreeItem = 50024,
        Custom = 50025, Group = 50026, Document = 50030, Window = 50032, Pane = 50033, Table = 50036, Separator = 50038;

    // Text
    public const int IsReadOnlyAttribute = 40015;
    public const int EndpointStart = 0;
    public const int EndpointEnd = 1;

    [DllImport("UIAutomationCore.dll")]
    public static extern nint UiaReturnRawElementProvider(nint hwnd, nint wParam, nint lParam, IRawElementProviderSimple? provider);

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaHostProviderFromHwnd(nint hwnd, out IRawElementProviderSimple? provider);

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaRaiseAutomationEvent(IRawElementProviderSimple provider, int eventId);

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaRaiseAutomationPropertyChangedEvent(IRawElementProviderSimple provider, int propertyId, [MarshalAs(UnmanagedType.Struct)] object? oldValue, [MarshalAs(UnmanagedType.Struct)] object? newValue);

    // Notifications, which a screen reader speaks without moving focus
    public const int NotificationKindOther = 4;
    public const int NotificationProcessingImportantMostRecent = 1;

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaRaiseNotificationEvent(IRawElementProviderSimple provider, int notificationKind, int notificationProcessing, [MarshalAs(UnmanagedType.BStr)] string displayString, [MarshalAs(UnmanagedType.BStr)] string activityId);

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaRaiseStructureChangedEvent(IRawElementProviderSimple provider, int changeType, int[] runtimeId, int runtimeIdLength);

    [LibraryImport("UIAutomationCore.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UiaClientsAreListening();

    [DllImport("UIAutomationCore.dll")]
    public static extern int UiaDisconnectProvider(IRawElementProviderSimple provider);

    [LibraryImport("UIAutomationCore.dll")]
    private static partial int UiaGetReservedNotSupportedValue(out nint value);

    /// <summary>The value a text attribute has when the provider does not support it.</summary>
    public static object NotSupported => _notSupported ??= GetNotSupported();

    private static object? _notSupported;

    private static object GetNotSupported()
    {
        UiaGetReservedNotSupportedValue(out var pointer);
        return Marshal.GetObjectForIUnknown(pointer);
    }
}
