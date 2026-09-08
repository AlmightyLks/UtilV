using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UtilV.Configuration;
using UtilV.Core;
using UtilV.Models;
using static UtilV.X11.X11Interop;

namespace UtilV.X11;

/// <summary>
/// Watches the CLIPBOARD selection and, when asked, owns it.
///
/// Both roles share one thread and one Display connection: selection requests can only be
/// answered by the connection that holds ownership, and Xlib connections are not
/// thread-safe.
/// </summary>
internal sealed class X11ClipboardService : IClipboardMonitor, IClipboardWriter, IDisposable
{
    private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);

    private readonly CancellationTokenSource _cts = new();
    private readonly object _pendingGate = new();

    private Thread? _thread;
    private IntPtr _display;
    private IntPtr _window;
    private int _xfixesEventBase;

    private IntPtr _clipboard, _targets, _incr, _utf8, _string, _textPlainUtf8, _imagePng, _transfer;

    /// <summary>What we serve while we own the selection. Guarded by <see cref="_pendingGate"/>.</summary>
    private ClipEntry? _owned;

    /// <summary>Set while we own the selection, so our own change notifications are ignored.</summary>
    private volatile bool _selfOwned;

    private volatile bool _claimRequested;

    private volatile int _maxPayloadBytes =
        UtilVSettings.DefaultMaxPayloadMegabytes * 1024 * 1024;

    public event EventHandler<ClipboardSnapshot>? Captured;

    /// <summary>Anything larger is refused rather than buffered. Read from the service thread.</summary>
    public int MaxPayloadBytes
    {
        get => _maxPayloadBytes;
        set => _maxPayloadBytes = Math.Max(1024, value);
    }

    public void Start()
    {
        if (_thread is not null)
            throw new InvalidOperationException("Service already started.");

        _thread = new Thread(Run) { IsBackground = true, Name = "UtilV clipboard" };
        _thread.Start();
    }

    public void Set(ClipEntry entry)
    {
        lock (_pendingGate)
            _owned = entry;

        // Ownership must be claimed on the service thread; the loop picks this up.
        _claimRequested = true;
        WakeLoop();
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(TimeSpan.FromSeconds(2));
        _cts.Dispose();
    }

    private void WakeLoop()
    {
        if (_display == IntPtr.Zero || _window == IntPtr.Zero)
            return;

        // Poking our own window generates an event, which breaks the loop out of poll().
        XChangeProperty(_display, _window, _transfer, _string, Format8, PropModeReplace, [0], 1);
        XFlush(_display);
    }

    // ------------------------------------------------------------ the X11 loop

    private void Run()
    {
        // One connection for both watching and owning, used only from this thread.
        _display = XOpenDisplay(IntPtr.Zero);

        if (_display == IntPtr.Zero)
        {
            Diagnostics.Log("clipboard: cannot open display");
            return;
        }

        // Selection traffic races other clients exiting, so errors must not be fatal.
        IgnoreErrors();

        // Intern every atom once; they are just interned strings the server hands back.
        _clipboard = XInternAtom(_display, "CLIPBOARD", false);
        _targets = XInternAtom(_display, "TARGETS", false);
        _incr = XInternAtom(_display, "INCR", false);
        _utf8 = XInternAtom(_display, "UTF8_STRING", false);
        _string = XInternAtom(_display, "STRING", false);
        _textPlainUtf8 = XInternAtom(_display, "text/plain;charset=utf-8", false);
        _imagePng = XInternAtom(_display, "image/png", false);
        _transfer = XInternAtom(_display, "UTILV_TRANSFER", false);

        // Selections need a window to own them and to receive replies on; 1x1 offscreen.
        _window = XCreateSimpleWindow(_display, XDefaultRootWindow(_display), -10, -10, 1, 1, 0, 0, 0);

        // Property changes are how INCR transfers signal each chunk.
        XSelectInput(_display, _window, PropertyChangeMask);

        // XFIXES is what makes the clipboard watchable at all; without it X has no signal.
        if (!XFixesQueryExtension(_display, out _xfixesEventBase, out _))
        {
            Diagnostics.Log("clipboard: XFIXES unavailable; cannot watch the selection");
            return;
        }

        // Ask to be told whenever any client takes ownership of CLIPBOARD.
        XFixesSelectSelectionInput(_display, _window, _clipboard, XFixesSetSelectionOwnerNotifyMask);
        XFlush(_display);

        int fd = XConnectionNumber(_display);
        var fds = new PollFd[1];
        var buffer = new byte[XEventSize];

        try
        {
            while (!_cts.IsCancellationRequested)
            {
                if (_claimRequested)
                {
                    _claimRequested = false;
                    ClaimOwnership();
                }

                // Sleep on the connection until an event arrives.
                if (XPending(_display) == 0)
                {
                    fds[0] = new PollFd { Fd = fd, Events = PollIn };
                    poll(fds, 1, 200);
                    continue;
                }

                XNextEvent(_display, ref buffer[0]);
                Dispatch(buffer);
            }
        }
        finally
        {
            // Drop the helper window and the connection together.
            if (_window != IntPtr.Zero)
                XDestroyWindow(_display, _window);

            XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }

    private void Dispatch(byte[] ev)
    {
        int type = TypeOf(ev);

        // XFIXES events are numbered from a base the server assigns at query time.
        if (type == _xfixesEventBase)
        {
            OnSelectionOwnerChanged(ev);
            return;
        }

        switch (type)
        {
            case SelectionRequest:
                OnSelectionRequest(ev);
                break;

            // Another client took the clipboard, so we are no longer serving it.
            case SelectionClear:
                _selfOwned = false;
                lock (_pendingGate) _owned = null;
                break;
        }
    }

    // --------------------------------------------------------------- watching

    private void OnSelectionOwnerChanged(byte[] ev)
    {
        var owner = Read(ev, Field.OwnerWindow);

        // Our own write, or a cleared selection; reading either back would churn history.
        if (owner == _window || _selfOwned || owner == IntPtr.Zero)
            return;

        var snapshot = ReadSelection();

        if (snapshot is not null)
            Captured?.Invoke(this, snapshot);
    }

    private ClipboardSnapshot? ReadSelection()
    {
        // Ask the owner what formats it can provide before asking for any of them.
        var available = RequestAtomList(_targets);

        if (available is null)
            return null;

        if (available.Contains(_imagePng))
        {
            var png = RequestBytes(_imagePng);
            return png is { Length: > 0 } ? ClipboardSnapshot.ForImage(png) : null;
        }

        // Text flavours in order of preference; the first one that yields bytes wins.
        foreach (var target in new[] { _utf8, _textPlainUtf8, _string })
        {
            if (!available.Contains(target))
                continue;

            var bytes = RequestBytes(target);

            if (bytes is not { Length: > 0 })
                continue;

            var text = Encoding.UTF8.GetString(bytes);
            return string.IsNullOrEmpty(text) ? null : ClipboardSnapshot.ForText(text);
        }

        return null;
    }

    private HashSet<IntPtr>? RequestAtomList(IntPtr target)
    {
        if (!ConvertAndWait(target))
            return null;

        var raw = ReadProperty(out _, out int format);

        if (raw is null || format != Format32)
            return null;

        // A TARGETS reply is a packed array of atoms, one long each.
        var atoms = new HashSet<IntPtr>();

        for (int i = 0; i + 8 <= raw.Length; i += 8)
            atoms.Add((IntPtr)BitConverter.ToInt64(raw, i));

        return atoms;
    }

    private byte[]? RequestBytes(IntPtr target)
    {
        if (!ConvertAndWait(target))
            return null;

        var raw = ReadProperty(out var actualType, out _);

        // Anything sizeable arrives in chunks; the first reply is only a size estimate.
        return actualType == _incr ? ReadIncremental() : raw;
    }

    /// <summary>Asks the owner to write one format into our property, and waits for it.</summary>
    private bool ConvertAndWait(IntPtr target)
    {
        // Clear the property first so a stale value cannot be mistaken for the reply.
        XDeleteProperty(_display, _window, _transfer);

        // Ask the current owner to convert the selection into this format.
        XConvertSelection(_display, _clipboard, target, _transfer, _window, IntPtr.Zero);
        XFlush(_display);

        // The owner is another process, which may be busy, frozen, or gone.
        if (!WaitFor(SelectionNotify, out var ev, ReadTimeout))
            return false;

        // A property of None means the owner refused this format.
        return Read(ev!, Field.NotifyProperty) != IntPtr.Zero;
    }

    /// <summary>
    /// INCR: the owner writes successive chunks to our property, signalling each with a
    /// PropertyNotify, and ends with a zero-length write.
    /// </summary>
    private byte[]? ReadIncremental()
    {
        var assembled = new List<byte>();

        // Deleting the property tells the owner we are ready for the first chunk.
        XDeleteProperty(_display, _window, _transfer);
        XFlush(_display);

        while (true)
        {
            if (!WaitFor(PropertyNotify, out var ev, ReadTimeout))
                return null;

            // Our own deletes raise PropertyNotify too; only new values carry data.
            if (BitConverter.ToInt32(ev!, Field.PropertyState) != PropertyNewValue)
                continue;

            var chunk = ReadProperty(out _, out _);

            // Deleting again acknowledges this chunk and asks for the next.
            XDeleteProperty(_display, _window, _transfer);
            XFlush(_display);

            // A zero-length chunk marks the end of the transfer.
            if (chunk is null || chunk.Length == 0)
                return assembled.ToArray();

            if (assembled.Count + chunk.Length > _maxPayloadBytes)
            {
                Diagnostics.Log($"clipboard: payload exceeds {_maxPayloadBytes} bytes, discarding");
                return null;
            }

            assembled.AddRange(chunk);
        }
    }

    private byte[]? ReadProperty(out IntPtr actualType, out int format)
    {
        // Read zero bytes first: the reply reports the real size in bytesAfter.
        if (XGetWindowProperty(_display, _window, _transfer, 0, 0, false, AnyPropertyType,
                out actualType, out format, out _, out var bytesAfter, out var probe) != 0)
            return null;

        if (probe != IntPtr.Zero)
            XFree(probe);

        int length = (int)bytesAfter;

        if (length == 0)
            return [];

        if (length > _maxPayloadBytes)
        {
            Diagnostics.Log($"clipboard: property of {length} bytes exceeds the cap, discarding");
            return null;
        }

        // Now read it for real; the length argument counts 32-bit units, not bytes.
        if (XGetWindowProperty(_display, _window, _transfer, 0, (length / 4) + 1, false, AnyPropertyType,
                out actualType, out format, out var items, out _, out var data) != 0)
            return null;

        if (data == IntPtr.Zero)
            return null;

        // For format 32 the client-side buffer holds C longs, 8 bytes here, even though
        // the server counts the property in 4-byte units. Using format/8 would read half
        // the elements.
        int bytesPerItem = format == Format32 ? IntPtr.Size : format / 8;
        int byteCount = (int)items * bytesPerItem;

        // Copy out of Xlib's buffer, then hand it back.
        var managed = new byte[byteCount];
        Marshal.Copy(data, managed, 0, byteCount);
        XFree(data);

        return managed;
    }

    /// <summary>
    /// Pumps events until the wanted one arrives. Selection requests are still answered
    /// while waiting, since we may own the clipboard at the same time.
    /// </summary>
    private bool WaitFor(int wantedType, out byte[]? evOut, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var buffer = new byte[XEventSize];
        var fds = new PollFd[1];
        int fd = XConnectionNumber(_display);

        while (DateTime.UtcNow < deadline && !_cts.IsCancellationRequested)
        {
            if (XPending(_display) == 0)
            {
                fds[0] = new PollFd { Fd = fd, Events = PollIn };
                poll(fds, 1, 50);
                continue;
            }

            XNextEvent(_display, ref buffer[0]);
            int type = TypeOf(buffer);

            if (type == wantedType)
            {
                evOut = (byte[])buffer.Clone();
                return true;
            }

            if (type == SelectionRequest)
                OnSelectionRequest(buffer);
        }

        Diagnostics.Log($"clipboard: timed out waiting for event {wantedType}");
        evOut = null;
        return false;
    }

    // ----------------------------------------------------------------- owning

    private void ClaimOwnership()
    {
        _selfOwned = true;

        // Tell the server we now own CLIPBOARD.
        XSetSelectionOwner(_display, _clipboard, _window, IntPtr.Zero);
        XFlush(_display);

        // Ownership can be lost immediately, so confirm rather than assume.
        if (XGetSelectionOwner(_display, _clipboard) != _window)
        {
            _selfOwned = false;
            Diagnostics.Log("clipboard: failed to take ownership");
        }
    }

    /// <summary>Another client is asking us for the clipboard contents in some format.</summary>
    private void OnSelectionRequest(byte[] ev)
    {
        var requestor = Read(ev, Field.RequestRequestor);
        var target = Read(ev, Field.RequestTarget);
        var property = Read(ev, Field.RequestProperty);
        var time = Read(ev, Field.RequestTime);

        // A requestor from before ICCCM 2.0 may send None, meaning "use the target atom".
        if (property == IntPtr.Zero)
            property = target;

        ClipEntry? entry;
        lock (_pendingGate)
            entry = _owned;

        bool served = entry is not null && Serve(requestor, target, property, entry);

        // Either way the requestor gets an answer; None as the property means refused.
        SendSelectionNotify(requestor, target, served ? property : IntPtr.Zero, time);
    }

    private bool Serve(IntPtr requestor, IntPtr target, IntPtr property, ClipEntry entry)
    {
        // TARGETS is the "what formats do you have" question every requestor asks first.
        if (target == _targets)
        {
            var offered = entry.Kind == ClipKind.Image
                ? new[] { _targets, _imagePng }
                : new[] { _targets, _utf8, _textPlainUtf8, _string };

            var payload = new byte[offered.Length * 8];

            for (int i = 0; i < offered.Length; i++)
                BitConverter.TryWriteBytes(payload.AsSpan(i * 8), offered[i].ToInt64());

            // Write the atom list onto the requestor's own window.
            XChangeProperty(_display, requestor, property, XInternAtom(_display, "ATOM", false),
                Format32, PropModeReplace, payload, offered.Length);
            return true;
        }

        if (entry.Kind == ClipKind.Image && target == _imagePng && entry.ImageBytes is { } png)
        {
            // Hand over the PNG bytes as-is.
            XChangeProperty(_display, requestor, property, _imagePng, Format8, PropModeReplace, png, png.Length);
            return true;
        }

        if (entry.Kind == ClipKind.Text && entry.Text is { } text
            && (target == _utf8 || target == _string || target == _textPlainUtf8))
        {
            // All three text flavours are served as the same UTF-8 bytes.
            var bytes = Encoding.UTF8.GetBytes(text);
            XChangeProperty(_display, requestor, property, target, Format8, PropModeReplace, bytes, bytes.Length);
            return true;
        }

        return false;
    }

    /// <summary>Tells the requestor the property is ready, which completes the handshake.</summary>
    private void SendSelectionNotify(IntPtr requestor, IntPtr target, IntPtr property, IntPtr time)
    {
        // Build the reply event by hand, field by field.
        var ev = new byte[XEventSize];
        Write(ev, 0, SelectionNotify);
        Write(ev, 16, 0);                                   // send_event
        Write(ev, 24, _display.ToInt64());
        Write(ev, Field.NotifyRequestor, requestor.ToInt64());
        Write(ev, Field.NotifySelection, _clipboard.ToInt64());
        Write(ev, Field.NotifyTarget, target.ToInt64());
        Write(ev, Field.NotifyProperty, property.ToInt64());
        Write(ev, Field.NotifyTime, time.ToInt64());

        XSendEvent(_display, requestor, false, 0, ref ev[0]);
        XFlush(_display);
    }
}
