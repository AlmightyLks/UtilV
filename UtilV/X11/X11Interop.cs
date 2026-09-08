using System;
using System.Runtime.InteropServices;

namespace UtilV.X11;

/// <summary>
/// Every Xlib call UtilV makes, in one place. Grouped by what the calls are for, so a
/// reader can find "the selection ones" or "the keyboard ones" without hunting.
/// </summary>
internal static class X11Interop
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXfixes = "libXfixes.so.3";
    private const string LibXtst = "libXtst.so.6";

    // ------------------------------------------------------------------ events

    public const int KeyPress = 2;
    public const int PropertyNotify = 28;
    public const int SelectionClear = 29;
    public const int SelectionRequest = 30;
    public const int SelectionNotify = 31;
    public const int MappingNotify = 34;

    /// <summary>XEvent is a union of 24 longs, and XNextEvent always writes all 192 bytes.</summary>
    public const int XEventSize = 192;

    /// <summary>
    /// Byte offsets of the fields UtilV reads out of an XEvent. Xlib gives no managed
    /// struct for these, so they are read positionally; every field here is a long.
    /// </summary>
    public static class Field
    {
        // XSelectionRequestEvent
        public const int RequestRequestor = 40;
        public const int RequestTarget = 56;
        public const int RequestProperty = 64;
        public const int RequestTime = 72;

        // XSelectionEvent, as sent back to a requestor
        public const int NotifyRequestor = 32;
        public const int NotifySelection = 40;
        public const int NotifyTarget = 48;
        public const int NotifyProperty = 56;
        public const int NotifyTime = 64;

        // XFixesSelectionNotifyEvent
        public const int OwnerWindow = 48;

        // XPropertyEvent: 0 = NewValue, 1 = Delete
        public const int PropertyState = 56;

        // XErrorEvent is a struct Xlib fills in, not a wire event, and has neither a
        // serial-before-display nor a send_event field: type, display, resourceid, serial,
        // then error_code at 32.
        public const int ErrorCode = 32;
    }

    public const int PropertyNewValue = 0;
    public const long PropertyChangeMask = 1L << 22;
    public const byte BadAccess = 10;

    // ------------------------------------------------------------ properties

    public const int AnyPropertyType = 0;
    public const int PropModeReplace = 0;
    public const int Format8 = 8;
    public const int Format32 = 32;

    // --------------------------------------------------------------- windows

    public const nuint CwOverrideRedirect = 1 << 9;
    public const int RevertToParent = 2;

    /// <summary>XSetWindowAttributes is 112 bytes on 64-bit, override_redirect at 88.</summary>
    public const int OverrideRedirectOffset = 88;

    // -------------------------------------------------------------- keyboard

    // Mod1 is Alt on every layout in practice. Lock, Mod2 and Mod5 are the toggles (Caps,
    // Num, Scroll) that would otherwise defeat a grab, which matches modifiers exactly.
    public const uint Mod1Mask = 1 << 3;
    public const uint LockMask = 1 << 1;
    public const uint Mod2Mask = 1 << 4;
    public const uint Mod5Mask = 1 << 7;

    public const int GrabModeAsync = 1;

    // ----------------------------------------------------------------- xfixes

    public const int XFixesSetSelectionOwnerNotifyMask = 1 << 0;

    // ------------------------------------------------------- connection, poll

    [DllImport(LibX11)] public static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport(LibX11)] public static extern int XCloseDisplay(IntPtr display);
    [DllImport(LibX11)] public static extern int XConnectionNumber(IntPtr display);
    [DllImport(LibX11)] public static extern int XFlush(IntPtr display);
    [DllImport(LibX11)] public static extern int XSync(IntPtr display, int discard);
    [DllImport(LibX11)] public static extern int XFree(IntPtr data);

    public const short PollIn = 0x001;

    [StructLayout(LayoutKind.Sequential)]
    public struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [DllImport("libc", SetLastError = true)]
    public static extern int poll([In, Out] PollFd[] fds, uint nfds, int timeout);

    // --------------------------------------------------------------- windows

    [DllImport(LibX11)] public static extern IntPtr XDefaultRootWindow(IntPtr display);
    [DllImport(LibX11)] public static extern int XDestroyWindow(IntPtr display, IntPtr window);
    [DllImport(LibX11)] public static extern int XRaiseWindow(IntPtr display, IntPtr window);
    [DllImport(LibX11)] public static extern int XSelectInput(IntPtr display, IntPtr window, long mask);

    [DllImport(LibX11)]
    public static extern IntPtr XCreateSimpleWindow(IntPtr display, IntPtr parent,
        int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);

    [DllImport(LibX11)]
    public static extern int XChangeWindowAttributes(IntPtr display, IntPtr window,
        nuint valueMask, ref byte attributes);

    [DllImport(LibX11)]
    public static extern int XQueryTree(IntPtr display, IntPtr window,
        out IntPtr root, out IntPtr parent, out IntPtr children, out uint childCount);

    [DllImport(LibX11)]
    public static extern int XQueryPointer(IntPtr display, IntPtr window,
        out IntPtr root, out IntPtr child, out int rootX, out int rootY,
        out int winX, out int winY, out uint mask);

    // ------------------------------------------------------- atoms, properties

    [DllImport(LibX11)] public static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport(LibX11)] public static extern int XDeleteProperty(IntPtr display, IntPtr window, IntPtr property);

    [DllImport(LibX11)]
    public static extern int XGetWindowProperty(IntPtr display, IntPtr window, IntPtr property,
        nint offset, nint length, bool delete, IntPtr requestedType,
        out IntPtr actualType, out int actualFormat, out nuint itemCount, out nuint bytesAfter,
        out IntPtr data);

    [DllImport(LibX11)]
    public static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property,
        IntPtr type, int format, int mode, byte[] data, int elementCount);

    // ------------------------------------------------------------- selections

    [DllImport(LibX11)] public static extern IntPtr XGetSelectionOwner(IntPtr display, IntPtr selection);
    [DllImport(LibX11)] public static extern int XSetSelectionOwner(IntPtr display, IntPtr selection, IntPtr owner, IntPtr time);

    [DllImport(LibX11)]
    public static extern int XConvertSelection(IntPtr display, IntPtr selection,
        IntPtr target, IntPtr property, IntPtr requestor, IntPtr time);

    [DllImport(LibXfixes)] public static extern bool XFixesQueryExtension(IntPtr display, out int eventBase, out int errorBase);
    [DllImport(LibXfixes)] public static extern void XFixesSelectSelectionInput(IntPtr display, IntPtr window, IntPtr selection, ulong mask);

    // ------------------------------------------------------------ event queue

    [DllImport(LibX11)] public static extern int XPending(IntPtr display);
    [DllImport(LibX11)] public static extern int XNextEvent(IntPtr display, ref byte eventReturn);
    [DllImport(LibX11)] public static extern int XSendEvent(IntPtr display, IntPtr window, bool propagate, long mask, ref byte send);

    // -------------------------------------------------------- keyboard, focus

    [DllImport(LibX11)] public static extern IntPtr XStringToKeysym(string name);
    [DllImport(LibX11)] public static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
    [DllImport(LibX11)] public static extern int XRefreshKeyboardMapping(ref byte mappingEvent);
    [DllImport(LibX11)] public static extern int XGetInputFocus(IntPtr display, out IntPtr focus, out int revertTo);
    [DllImport(LibX11)] public static extern int XSetInputFocus(IntPtr display, IntPtr focus, int revertTo, IntPtr time);

    [DllImport(LibX11)]
    public static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow,
        bool ownerEvents, int pointerMode, int keyboardMode);

    [DllImport(LibX11)]
    public static extern int XUngrabKey(IntPtr display, int keycode, uint modifiers, IntPtr grabWindow);

    [DllImport(LibXtst)] public static extern int XTestFakeKeyEvent(IntPtr display, uint keycode, bool isPress, ulong delay);

    // ----------------------------------------------------------------- errors

    public delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [DllImport(LibX11)] private static extern IntPtr XSetErrorHandler(XErrorHandler handler);

    private static XErrorHandler? s_handler;
    private static volatile int s_lastError;

    /// <summary>
    /// Installs the one error handler, once, and holds the delegate alive. Xlib's default
    /// handler calls exit(), and UtilV's calls routinely race windows disappearing, so a
    /// protocol error must never be fatal.
    ///
    /// There is deliberately only one: XSetErrorHandler is process-wide, so a second
    /// caller installing its own would silently replace the first. The handler swallows
    /// everything and records the code, which is all any caller here needs.
    /// </summary>
    public static void IgnoreErrors()
    {
        if (s_handler is not null)
            return;

        s_handler = static (_, error) =>
        {
            s_lastError = Marshal.ReadByte(error, Field.ErrorCode);
            return 0;
        };

        XSetErrorHandler(s_handler);
    }

    /// <summary>The code of the last protocol error, for calls that expect to be refused.</summary>
    public static int LastError => s_lastError;

    /// <summary>Resets <see cref="LastError"/> before a call whose failure matters.</summary>
    public static void ClearLastError() => s_lastError = 0;

    // ------------------------------------------------------------- event reads

    /// <summary>The event type, which is the first field of every XEvent.</summary>
    public static int TypeOf(byte[] ev) => BitConverter.ToInt32(ev, 0);

    /// <summary>Reads one long-sized field out of an event at a <see cref="Field"/> offset.</summary>
    public static IntPtr Read(byte[] ev, int offset) => (IntPtr)BitConverter.ToInt64(ev, offset);

    /// <summary>Writes one long-sized field, for the events UtilV sends itself.</summary>
    public static void Write(byte[] ev, int offset, long value) =>
        BitConverter.TryWriteBytes(ev.AsSpan(offset), value);
}
