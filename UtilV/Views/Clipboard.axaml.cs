using System;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using UtilV.Models;
using UtilV.ViewModels;
using UtilV.X11;

namespace UtilV.Views;

public partial class Clipboard : Window
{
    /// <summary>Focus can take a moment to land after mapping; ignore that window.</summary>
    private static readonly TimeSpan FocusSettle = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherTimer _focusWatch;
    private DateTime _shownAt;

    static Clipboard()
    {
        // A focused ListBox marks Enter and the arrow keys handled, so a window-level
        // OnKeyDown override never sees them. A class handler with handledEventsToo does.
        InputElement.KeyDownEvent.AddClassHandler<Clipboard>(
            (popup, e) => popup.HandleKey(e), handledEventsToo: true);
    }

    public Clipboard()
    {
        InitializeComponent();

        // Dismiss on focus loss, the way Win+V does. Avalonia's Deactivated cannot be used:
        // an override-redirect window is unmanaged, so the window manager protocols its
        // activation tracking depends on never fire. X is asked directly instead, polled
        // only while the popup is open.
        _focusWatch = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _focusWatch.Tick += OnFocusWatchTick;
    }

    /// <summary>Window this popup should paste into, sampled before the popup appeared.</summary>
    public IntPtr TargetWindow { get; private set; }

    /// <summary>
    /// Creates the native window once, off-screen, so it can be made override-redirect
    /// while still unmapped. The flag is only read at map time.
    /// </summary>
    public void Prewarm()
    {
        Position = new PixelPoint(-32000, -32000);
        Show();
        Hide();

        if (TryGetPlatformHandle()?.Handle is { } handle)
            X11WindowProperties.SetOverrideRedirect(handle, true);
    }

    /// <summary>
    /// Shows the popup at a screen position, records the window a later paste should
    /// target, and starts watching for focus leaving.
    /// </summary>
    public void ShowAt(PixelPoint position, IntPtr targetWindow)
    {
        TargetWindow = targetWindow;
        Position = position;

        // Reopening should land on the history, not wherever the user left the panes.
        if (DataContext is ClipboardViewModel model)
            model.IsSettingsOpen = false;

        // Focusing a control inside the settings pane scrolls it, so the pane would
        // otherwise reopen wherever it was left.
        this.FindControl<ScrollViewer>("SettingsScroll")?.ScrollToHome();

        Show();
        Activate();

        _shownAt = DateTime.UtcNow;
        _focusWatch.Start();

        // The window manager ignores override-redirect windows, so raising and focusing
        // are ours to do.
        if (TryGetPlatformHandle()?.Handle is { } handle)
        {
            X11WindowProperties.Raise(handle);
            X11WindowProperties.FocusWindow(handle);
        }
    }

    public void HidePopup()
    {
        _focusWatch.Stop();
        Hide();
    }

    private void OnFocusWatchTick(object? sender, EventArgs e)
    {
        if (!IsVisible || DateTime.UtcNow - _shownAt < FocusSettle)
            return;

        if (TryGetPlatformHandle()?.Handle is not { } handle)
            return;

        if (!X11WindowProperties.IsFocusWithin(handle))
            HidePopup();
    }

    private void OnCloseClicked(object? sender, RoutedEventArgs e) => HidePopup();

    /// <summary>
    /// A single click pastes and closes. The clicked row is used rather than the list's
    /// selection, so a click always acts on what is under the pointer.
    /// </summary>
    private void OnEntryTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ClipboardViewModel model
            && (e.Source as Control)?.DataContext is ClipEntry entry)
        {
            model.ActivateCommand.Execute(entry);
            e.Handled = true;
        }
    }

    private void OnRowPinTapped(object? sender, TappedEventArgs e) =>
        RunRowCommand(sender, e, (model, entry) => model.TogglePinCommand.Execute(entry));

    private void OnRowDeleteTapped(object? sender, TappedEventArgs e) =>
        RunRowCommand(sender, e, (model, entry) => model.DeleteCommand.Execute(entry));

    /// <summary>
    /// Runs a row button's command. Tapped is a gesture event raised on the list rather
    /// than the button, so marking it handled here is what stops it also reaching the
    /// row and pasting.
    /// </summary>
    private void RunRowCommand(object? sender, TappedEventArgs e, Action<ClipboardViewModel, ClipEntry> run)
    {
        e.Handled = true;

        if (DataContext is ClipboardViewModel model
            && (sender as Control)?.DataContext is ClipEntry entry)
        {
            run(model, entry);
        }
    }

    /// <summary>
    /// Keeps only digits in a limit box. Filtering the text itself rather than the
    /// keystrokes covers pasting and drag-and-drop too, so the int binding can never be
    /// handed something it cannot convert.
    /// </summary>
    public static string DigitsOnly(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var kept = new StringBuilder(text.Length);

        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c))
                kept.Append(c);
        }

        return kept.ToString();
    }

    private void OnNumberTextChanged(object? sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox box)
            return;

        var digits = DigitsOnly(box.Text);

        // Assigning re-enters this handler, so only touch the box when something changed.
        if (digits == box.Text)
            return;

        int removed = (box.Text?.Length ?? 0) - digits.Length;
        int caret = box.CaretIndex - removed;

        box.Text = digits;
        box.CaretIndex = Math.Clamp(caret, 0, digits.Length);
    }

    /// <summary>
    /// An empty box would reach the binding as "" and fail to convert, so the last good
    /// value is put back when the field is left blank.
    /// </summary>
    private void OnNumberLostFocus(object? sender, RoutedEventArgs e)
    {
        if (sender is not TextBox box || DataContext is not ClipboardViewModel model)
            return;

        if (!string.IsNullOrEmpty(box.Text))
            return;

        box.Text = box.Name == "PayloadLimitBox"
            ? model.MaxPayloadMegabytes.ToString(CultureInfo.InvariantCulture)
            : model.MaxHistoryEntries.ToString(CultureInfo.InvariantCulture);
    }

    private void HandleKey(KeyEventArgs e)
    {
        // Escape is the only key command: rows are activated by clicking, and pin and
        // remove are buttons on the hovered row.
        if (e.Key != Key.Escape)
            return;

        HidePopup();
        e.Handled = true;
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        // The popup is created once and reused, and an Avalonia Window cannot be reopened
        // after closing, so a real close (Alt+F4) becomes a hide.
        if (!e.IsProgrammatic)
        {
            e.Cancel = true;
            HidePopup();
        }

        base.OnClosing(e);
    }
}
