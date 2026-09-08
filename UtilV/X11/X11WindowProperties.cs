using System;
using static UtilV.X11.X11Interop;

namespace UtilV.X11;

/// <summary>
/// The window and input calls Avalonia does not expose. Uses its own Display connection
/// and must only be called from the UI thread.
/// </summary>
internal static class X11WindowProperties
{
    private static IntPtr s_display;

    private static bool Connected()
    {
        if (s_display != IntPtr.Zero)
            return true;

        // Open our own connection; Avalonia does not hand its own out.
        s_display = XOpenDisplay(IntPtr.Zero);

        // Focus and attribute calls race windows being unmapped, so never die on an error.
        IgnoreErrors();

        return s_display != IntPtr.Zero;
    }

    /// <summary>
    /// Takes the window out of the window manager's control, so nothing can defer or
    /// reclaim it. Must be set while the window is unmapped: the flag is read at map time.
    /// </summary>
    public static void SetOverrideRedirect(IntPtr window, bool enabled)
    {
        if (window == IntPtr.Zero || !Connected())
            return;

        // Fill an oversized XSetWindowAttributes and set only the override_redirect field.
        var attributes = new byte[128];
        BitConverter.TryWriteBytes(attributes.AsSpan(OverrideRedirectOffset), enabled ? 1 : 0);

        // Apply that one attribute to the window.
        XChangeWindowAttributes(s_display, window, CwOverrideRedirect, ref attributes[0]);
        XFlush(s_display);
    }

    /// <summary>
    /// Gives the window keyboard focus directly. An override-redirect window is invisible
    /// to the window manager, so nothing will focus it on our behalf.
    /// </summary>
    public static void FocusWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !Connected())
            return;

        // Hand focus to the window, reverting to its parent if it disappears.
        XSetInputFocus(s_display, window, RevertToParent, IntPtr.Zero);
        XFlush(s_display);
    }

    /// <summary>Puts the window on top, which is also ours to do while unmanaged.</summary>
    public static void Raise(IntPtr window)
    {
        if (window == IntPtr.Zero || !Connected())
            return;

        XRaiseWindow(s_display, window);
        XFlush(s_display);
    }

    /// <summary>
    /// Whether keyboard focus is on the given window or one of its descendants. Asking X
    /// directly is the only reliable signal for an override-redirect window, which never
    /// gets the window manager activation events Avalonia reports.
    /// </summary>
    public static bool IsFocusWithin(IntPtr window)
    {
        if (window == IntPtr.Zero || !Connected())
            return false;

        // Ask which window holds focus right now.
        XGetInputFocus(s_display, out var focus, out _);

        // 0 = None, 1 = PointerRoot; neither is a real window.
        if (focus == IntPtr.Zero || focus == new IntPtr(1))
            return false;

        // Walk up the parents, since focus often sits on a child of our window.
        var current = focus;

        for (int depth = 0; depth < 32 && current != IntPtr.Zero; depth++)
        {
            if (current == window)
                return true;

            // Step to the parent; XQueryTree allocates the child list, so free it.
            if (XQueryTree(s_display, current, out _, out var parent, out var children, out _) == 0)
                break;

            if (children != IntPtr.Zero)
                XFree(children);

            current = parent;
        }

        return false;
    }

    /// <summary>Pointer position in root coordinates, for placing the popup.</summary>
    public static (int X, int Y) QueryPointer()
    {
        if (!Connected())
            return (0, 0);

        // Pointer position relative to the root window, which is the whole desktop.
        XQueryPointer(s_display, XDefaultRootWindow(s_display), out _, out _,
            out int x, out int y, out _, out _, out _);

        return (x, y);
    }

    /// <summary>The window that currently has input focus, and so the paste target.</summary>
    public static IntPtr GetInputFocus()
    {
        if (!Connected())
            return IntPtr.Zero;

        XGetInputFocus(s_display, out var focus, out _);
        return focus;
    }
}
