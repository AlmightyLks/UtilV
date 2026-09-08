using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using UtilV.Configuration;
using UtilV.Core;
using UtilV.Ipc;
using UtilV.ViewModels;
using UtilV.Views;
using UtilV.X11;

namespace UtilV;

public partial class App : Application
{
    private readonly ClipHistory _history = new();

    private CommandServer? _commands;
    private X11GlobalHotkey? _hotkey;
    private X11ClipboardService? _clipboardService;
    private X11PasteEmitter? _pasteEmitter;
    private ClipboardViewModel? _viewModel;
    private Views.Clipboard? _clipboard;
    private bool _shuttingDown;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        Dispatcher.UIThread.UnhandledException += OnDispatcherUnhandledException;

        // Rider's XAML previewer loads this assembly and runs App initialization in its
        // own process, so anything with a system-wide side effect stays out of design mode.
        if (!Design.IsDesignMode &&
            ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void Start()
    {
        // Alt+V is registered with the X server directly, so it works under any window
        // manager with nothing to configure. The socket stays regardless: it is how
        // `utilv --toggle` works, and the only way in when the grab is refused.
        _hotkey = new X11GlobalHotkey();
        _hotkey.Pressed += OnHotkeyPressed;

        if (!_hotkey.Start())
        {
            _hotkey.Dispose();
            _hotkey = null;

            Console.Error.WriteLine(
                "UtilV: could not register Alt+V, so another application is holding it. " +
                "Bind a key to 'utilv --toggle' in your desktop settings instead.");
        }

        _commands = new CommandServer();
        _commands.CommandReceived += OnCommandReceived;

        if (!_commands.Start())
        {
            Console.Error.WriteLine(
                $"UtilV: could not listen on {CommandEndpoint.SocketPath}; " +
                "the toggle command will not work.");
        }

        var settings = SettingsStore.Load();
        _history.MaxUnpinned = settings.MaxHistoryEntries;

        var service = new X11ClipboardService { MaxPayloadBytes = settings.MaxPayloadBytes };
        _clipboardService = service;
        _pasteEmitter = new X11PasteEmitter();

        // Captures arrive on the service thread; the history is bound to the popup, so it
        // may only be touched from the UI thread.
        service.Captured += (_, snapshot) =>
            Dispatcher.UIThread.Post(() => _history.Add(snapshot));
        service.Start();

        _viewModel = new ClipboardViewModel(_history, service, settings);
        _viewModel.PasteRequested += OnPasteRequested;
        _viewModel.SettingsChanged += OnSettingsChanged;

        _clipboard = new Views.Clipboard { DataContext = _viewModel };
        _clipboard.Prewarm();

#if DEBUG
        // A development affordance. A release build is driven purely by the bound
        // shortcut, so it stays out of the user's notification area.
        CreateTrayIcon();
#endif
    }

    // Raised on the hotkey thread.
    private void OnHotkeyPressed(object? sender, EventArgs e) =>
        Dispatcher.UIThread.Post(ToggleClipboard);

    // Raised on the command-socket thread.
    private void OnCommandReceived(object? sender, string command)
    {
        if (string.Equals(command, CommandEndpoint.Toggle, StringComparison.OrdinalIgnoreCase))
            Dispatcher.UIThread.Post(ToggleClipboard);
    }

    private void ToggleClipboard()
    {
        if (_clipboard is null)
            return;

        if (_clipboard.IsVisible)
        {
            _clipboard.HidePopup();
            return;
        }

        var (pointerX, pointerY) = X11WindowProperties.QueryPointer();

        // The focused window has to be sampled now: once the popup is up it is the
        // focused window, and the paste target is lost.
        _clipboard.ShowAt(
            PlaceAtPointer(_clipboard, pointerX, pointerY),
            X11WindowProperties.GetInputFocus());
    }

    /// <summary>
    /// Places the popup the way a context menu opens: its top-left corner on the cursor.
    /// When there is no room below, the bottom-left corner goes there instead, so the
    /// popup stays attached to the pointer rather than sliding to the middle of the
    /// screen.
    /// </summary>
    private static PixelPoint PlaceAtPointer(Window window, int pointerX, int pointerY)
    {
        var screen = window.Screens.ScreenFromPoint(new PixelPoint(pointerX, pointerY))
                     ?? window.Screens.Primary;

        if (screen is null)
            return new PixelPoint(pointerX, pointerY);

        // Bounds, not WorkingArea: _NET_WORKAREA is one rectangle for the whole desktop,
        // so with monitors of differing height it reports the taller one too short. The
        // popup is override-redirect anyway, so panel struts do not constrain it.
        var area = screen.Bounds;
        int width = (int)(window.Width * screen.Scaling);
        int height = (int)(window.Height * screen.Scaling);

        int x = pointerX;
        int y = pointerY;

        if (y + height > area.Y + area.Height)
            y = pointerY - height;

        if (x + width > area.X + area.Width)
            x = pointerX - width;

        // Only reached when the popup fits on neither side of the pointer.
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.X + area.Width - width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Y + area.Height - height));

        return new PixelPoint(x, y);
    }

    /// <summary>
    /// The entry is already on the clipboard by this point. Hide first: the popup holds
    /// input focus, and the paste has to land in the window that had it before.
    /// </summary>
    private void OnPasteRequested(object? sender, EventArgs e)
    {
        if (_clipboard is null)
            return;

        var target = _clipboard.TargetWindow;
        _clipboard.HidePopup();

        if (target != IntPtr.Zero)
            _pasteEmitter?.PasteInto(target);
    }

    /// <summary>
    /// Applies an edited limit and persists it. The view model raises the change rather
    /// than writing the file itself, so it stays free of IO.
    /// </summary>
    private void OnSettingsChanged(object? sender, UtilVSettings settings)
    {
        var clamped = settings.Clamped();

        _history.MaxUnpinned = clamped.MaxHistoryEntries;

        if (_clipboardService is not null)
            _clipboardService.MaxPayloadBytes = clamped.MaxPayloadBytes;

        SettingsStore.Save(clamped);
    }

    // Avalonia's DBusTrayIconImpl.WatchAsync is `async void`, and its Dispose() cancels
    // the watch before setting _isDisposed, so the resulting TaskCanceledException misses
    // that class's own filters and lands here. Suppressed only while exiting: a blanket
    // suppression would eat cancellations from the clipboard and INCR transfer paths.
    private void OnDispatcherUnhandledException(object? sender, DispatcherUnhandledExceptionEventArgs e)
    {
        if (_shuttingDown && e.Exception is OperationCanceledException)
            e.Handled = true;
    }

    public void Exit(object? sender, EventArgs eventArgs)
    {
        _shuttingDown = true;

        _hotkey?.Dispose();
        _hotkey = null;

        _commands?.Dispose();
        _commands = null;

        _clipboardService?.Dispose();
        _clipboardService = null;

        _pasteEmitter?.Dispose();
        _pasteEmitter = null;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }

#if DEBUG
    private TrayIcon? _trayIcon;

    // Opened from the tray, which carries no pointer or focus context, so the popup lands
    // centred rather than at the cursor.
    private void OpenClipboardHistory(object? sender, EventArgs eventArgs)
    {
        if (_clipboard is null)
            return;

        if (_clipboard.IsVisible)
        {
            _clipboard.Activate();
            return;
        }

        int x = 0, y = 0;

        if (_clipboard.Screens.Primary is { } screen)
        {
            var area = screen.WorkingArea;
            x = area.X + (area.Width - (int)(_clipboard.Width * screen.Scaling)) / 2;
            y = area.Y + (area.Height - (int)(_clipboard.Height * screen.Scaling)) / 2;
        }

        _clipboard.ShowAt(new PixelPoint(x, y), IntPtr.Zero);
    }

    private void CreateTrayIcon()
    {
        var history = new NativeMenuItem("Clipboard history");
        history.Click += OpenClipboardHistory;

        var exit = new NativeMenuItem("Exit");
        exit.Click += Exit;

        var menu = new NativeMenu();
        menu.Add(history);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(exit);

        _trayIcon = new TrayIcon { ToolTipText = "UtilV", Menu = menu };

        try
        {
            using var stream = AssetLoader.Open(new Uri("avares://utilv/Assets/icons/utilv-32.png"));
            _trayIcon.Icon = new WindowIcon(stream);
        }
        catch (Exception ex)
        {
            Diagnostics.Log($"tray: icon unavailable ({ex.GetType().Name})");
        }

        _trayIcon.Clicked += OpenClipboardHistory;
        TrayIcon.SetIcons(this, [_trayIcon]);
    }
#endif
}
