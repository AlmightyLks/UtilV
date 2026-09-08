using System;
using System.Threading;
using UtilV.Core;
using static UtilV.X11.X11Interop;

namespace UtilV.X11;

/// <summary>
/// Restores focus to the window that was active before the popup opened, then synthesises
/// Ctrl+V into it.
/// </summary>
internal sealed class X11PasteEmitter : IPasteEmitter, IDisposable
{
    /// <summary>
    /// Focus changes are asynchronous, and a paste that lands before the target has focus
    /// goes to the wrong window, or nowhere.
    /// </summary>
    private static readonly TimeSpan FocusSettle = TimeSpan.FromMilliseconds(60);

    private IntPtr _display;

    public void PasteInto(IntPtr window)
    {
        if (window == IntPtr.Zero || !Connected())
            return;

        // Put focus back where it was before the popup took it.
        XSetInputFocus(_display, window, RevertToParent, IntPtr.Zero);

        // Wait for the server to apply it, then give the target a moment to react.
        XSync(_display, 0);
        Thread.Sleep(FocusSettle);

        // Look up the keycodes for Ctrl and V on the current layout.
        var control = XKeysymToKeycode(_display, XStringToKeysym("Control_L"));
        var v = XKeysymToKeycode(_display, XStringToKeysym("v"));

        if (control == 0 || v == 0)
        {
            Diagnostics.Log("paste: could not map Ctrl or V on this layout");
            return;
        }

        // Synthesise the press and release pair that makes up Ctrl+V.
        XTestFakeKeyEvent(_display, control, true, 0);
        XTestFakeKeyEvent(_display, v, true, 0);
        XTestFakeKeyEvent(_display, v, false, 0);
        XTestFakeKeyEvent(_display, control, false, 0);
        XFlush(_display);

        Diagnostics.Log($"paste: sent Ctrl+V to window 0x{window.ToInt64():x}");
    }

    private bool Connected()
    {
        if (_display != IntPtr.Zero)
            return true;

        // Own connection, so focus changes do not interleave with the clipboard thread.
        _display = XOpenDisplay(IntPtr.Zero);

        // Focusing a window that just disappeared is a protocol error, not a crash.
        IgnoreErrors();

        return _display != IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_display == IntPtr.Zero)
            return;

        XCloseDisplay(_display);
        _display = IntPtr.Zero;
    }
}
