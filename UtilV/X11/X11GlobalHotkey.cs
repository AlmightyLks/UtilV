using System;
using System.Threading;
using static UtilV.X11.X11Interop;

namespace UtilV.X11;

/// <summary>
/// Registers Alt+V with the X server, so the shortcut works under any X11 window manager
/// without the user configuring anything.
///
/// The grab is taken on the root window, which is a server-level facility rather than a
/// desktop feature, so it does not depend on the window manager cooperating. It can still
/// be refused: X allows only one client to hold a given combination, and a second grab
/// fails with BadAccess. The --toggle command remains as the fallback for that case, and
/// for Wayland, where global grabs do not exist at all.
/// </summary>
internal sealed class X11GlobalHotkey : IDisposable
{
    /// <summary>
    /// A grab matches modifiers exactly, so every combination of the lock keys has to be
    /// grabbed separately or the shortcut dies the moment Num Lock is on.
    /// </summary>
    private static readonly uint[] LockCombinations =
    [
        0,
        LockMask,
        Mod2Mask,
        Mod5Mask,
        LockMask | Mod2Mask,
        LockMask | Mod5Mask,
        Mod2Mask | Mod5Mask,
        LockMask | Mod2Mask | Mod5Mask,
    ];

    private readonly CancellationTokenSource _cts = new();

    private Thread? _thread;
    private IntPtr _display;
    private IntPtr _root;
    private int _keycode;

    /// <summary>Raised on the hotkey thread each time the combination is pressed.</summary>
    public event EventHandler? Pressed;

    /// <summary>
    /// Takes the grab and starts listening. Returns false when the combination is already
    /// held by another client, or when X is unavailable.
    /// </summary>
    public bool Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("Hotkey already started.");

        // Own connection, since this thread blocks in poll waiting for key presses.
        _display = XOpenDisplay(IntPtr.Zero);

        if (_display == IntPtr.Zero)
        {
            Diagnostics.Log("hotkey: cannot open display");
            return false;
        }

        // Xlib would otherwise exit the process over a key another client already holds.
        IgnoreErrors();

        // Grabs are registered against the root window, the parent of every other window.
        _root = XDefaultRootWindow(_display);

        // Find which physical key currently produces "v".
        _keycode = XKeysymToKeycode(_display, XStringToKeysym("v"));

        if (_keycode == 0)
        {
            Diagnostics.Log("hotkey: this layout has no V key");
            Close();
            return false;
        }

        if (!Grab())
        {
            Close();
            return false;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "UtilV hotkey" };
        _thread.Start();

        Diagnostics.Log("hotkey: Alt+V registered");
        return true;
    }

    private bool Grab()
    {
        ClearLastError();

        // Claim Alt+V once per lock-key combination.
        foreach (var locks in LockCombinations)
        {
            XGrabKey(_display, _keycode, Mod1Mask | locks, _root,
                false, GrabModeAsync, GrabModeAsync);
        }

        // Grabs are asynchronous, so a refusal only arrives once the requests are flushed.
        XSync(_display, 0);

        if (LastError != BadAccess)
            return true;

        Diagnostics.Log("hotkey: Alt+V is already held by another application");
        Ungrab();
        return false;
    }

    private void Ungrab()
    {
        // Release every combination claimed above.
        foreach (var locks in LockCombinations)
            XUngrabKey(_display, _keycode, Mod1Mask | locks, _root);

        XSync(_display, 0);
    }

    private void Run()
    {
        int fd = XConnectionNumber(_display);
        var fds = new PollFd[1];
        var buffer = new byte[XEventSize];

        while (!_cts.IsCancellationRequested)
        {
            // Sleep on the connection until something arrives, so idling costs nothing.
            if (XPending(_display) == 0)
            {
                fds[0] = new PollFd { Fd = fd, Events = PollIn };
                poll(fds, 1, 200);
                continue;
            }

            // Take the next event off the queue.
            XNextEvent(_display, ref buffer[0]);

            switch (TypeOf(buffer))
            {
                case KeyPress:
                    Pressed?.Invoke(this, EventArgs.Empty);
                    break;

                // The keycode for V moves when the layout changes, so the old grab would
                // be pointing at whatever key now occupies that position.
                case MappingNotify:
                    XRefreshKeyboardMapping(ref buffer[0]);
                    Regrab();
                    break;
            }
        }
    }

    private void Regrab()
    {
        int keycode = XKeysymToKeycode(_display, XStringToKeysym("v"));

        if (keycode == 0 || keycode == _keycode)
            return;

        Ungrab();
        _keycode = keycode;
        Grab();

        Diagnostics.Log($"hotkey: layout changed, V is now keycode {keycode}");
    }

    private void Close()
    {
        if (_display == IntPtr.Zero)
            return;

        XCloseDisplay(_display);
        _display = IntPtr.Zero;
    }

    public void Dispose()
    {
        _cts.Cancel();

        // Only touch the connection once the thread that reads from it has stopped: Xlib
        // is not thread-safe, so closing it underneath a live XNextEvent would crash.
        bool stopped = _thread?.Join(TimeSpan.FromSeconds(1)) ?? true;

        if (stopped && _display != IntPtr.Zero)
        {
            // Hand the combination back before dropping the connection.
            Ungrab();
            Close();
        }
        else if (!stopped)
        {
            Diagnostics.Log("hotkey: thread still running, leaving the display to process exit");
        }

        _cts.Dispose();
    }
}
