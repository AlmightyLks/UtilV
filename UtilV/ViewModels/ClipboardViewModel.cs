using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.IO;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UtilV.Configuration;
using UtilV.Core;
using UtilV.Models;

namespace UtilV.ViewModels;

internal sealed partial class ClipboardViewModel : ObservableObject
{
    private readonly ClipHistory _history;
    private readonly IClipboardWriter _writer;

    /// <summary>Raised once an entry is on the clipboard and should be pasted.</summary>
    public event EventHandler? PasteRequested;

    /// <summary>Raised when a limit is edited, so the owner can apply and persist it.</summary>
    public event EventHandler<UtilVSettings>? SettingsChanged;

    public ClipboardViewModel(ClipHistory history, IClipboardWriter writer, UtilVSettings settings)
    {
        _history = history;
        _writer = writer;
        Entries = history.Entries;

        // Seeded without notifying, so loading saved values does not look like a user edit.
        _maxHistoryEntries = settings.MaxHistoryEntries;
        _maxPayloadMegabytes = settings.MaxPayloadMegabytes;

        // IsEmpty is computed, so the placeholder and the clear button would never update.
        ((INotifyCollectionChanged)Entries).CollectionChanged +=
            (_, _) => OnPropertyChanged(nameof(IsEmpty));
    }

    public ReadOnlyObservableCollection<ClipEntry> Entries { get; }

    public bool IsEmpty => Entries.Count == 0;

    /// <summary>Which pane the popup is showing. It is one window with two faces.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PaneTitle))]
    private bool _isSettingsOpen;

    public string PaneTitle => IsSettingsOpen ? "Settings" : "Clipboard history";

    [ObservableProperty]
    private int _maxHistoryEntries;

    [ObservableProperty]
    private int _maxPayloadMegabytes;

    public string HistoryRangeHint =>
        $"{UtilVSettings.MinHistoryEntries} to {UtilVSettings.MaxHistoryEntriesLimit}";

    public string PayloadRangeHint =>
        $"{UtilVSettings.MinPayloadMegabytes} to {UtilVSettings.MaxPayloadMegabytesLimit} MB";

    // Re-assigning re-enters this with the clamped value, which then publishes once.
    partial void OnMaxHistoryEntriesChanged(int value)
    {
        int clamped = Math.Clamp(value, UtilVSettings.MinHistoryEntries, UtilVSettings.MaxHistoryEntriesLimit);

        if (clamped != value)
            MaxHistoryEntries = clamped;
        else
            PublishSettings();
    }

    partial void OnMaxPayloadMegabytesChanged(int value)
    {
        int clamped = Math.Clamp(value, UtilVSettings.MinPayloadMegabytes, UtilVSettings.MaxPayloadMegabytesLimit);

        if (clamped != value)
            MaxPayloadMegabytes = clamped;
        else
            PublishSettings();
    }

    private void PublishSettings()
    {
        SettingsChanged?.Invoke(this, new UtilVSettings
        {
            MaxHistoryEntries = MaxHistoryEntries,
            MaxPayloadMegabytes = MaxPayloadMegabytes,
        });
    }

    public string SettingsPath => SettingsStore.Path;

    /// <summary>
    /// The version this build was stamped with, for the settings pane. Read once, since it
    /// cannot change while the process runs, but exposed per instance because a binding
    /// resolves against the data context rather than the type.
    /// </summary>
    public string Version => s_version;

    private static readonly string s_version = ReadVersion();

    private static string ReadVersion()
    {
        var assembly = typeof(ClipboardViewModel).Assembly;

        // The informational version is the one that keeps a suffix like "-beta"; it can
        // also carry "+<commit>" build metadata, which is noise on screen.
        var informational = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrEmpty(informational))
        {
            int metadata = informational.IndexOf('+');
            return metadata < 0 ? informational : informational[..metadata];
        }

        return assembly.GetName().Version?.ToString(3) ?? "unknown";
    }

    /// <summary>
    /// The command to bind to a keyboard shortcut. Prefers the bare name once the binary
    /// is on PATH: an absolute path into a build directory breaks as soon as it is
    /// published elsewhere.
    /// </summary>
    public string ToggleCommandLine =>
        IsInstalledOnPath ? $"{ExecutableName} --toggle" : $"{ExecutablePath} --toggle";

    public bool IsInstalledOnPath => IsOnPath(ExecutableName);

    private static string ExecutablePath => Environment.ProcessPath ?? ExecutableName;

    private static string ExecutableName =>
        Path.GetFileName(Environment.ProcessPath) is { Length: > 0 } name ? name : "utilv";

    private static bool IsOnPath(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");

        if (string.IsNullOrEmpty(path))
            return false;

        foreach (var directory in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrEmpty(directory))
                continue;

            try
            {
                if (File.Exists(Path.Combine(directory, name)))
                    return true;
            }
            catch (ArgumentException)
            {
                // A malformed PATH entry is not worth failing over.
            }
        }

        return false;
    }

    [RelayCommand]
    private void ToggleSettings() => IsSettingsOpen = !IsSettingsOpen;

    [RelayCommand]
    private void Activate(ClipEntry? entry)
    {
        if (entry is null)
            return;

        _writer.Set(entry);

        // Picking an entry counts as copying it. This has to be explicit: the monitor
        // ignores the selection change we just caused ourselves.
        _history.Promote(entry);

        PasteRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Delete(ClipEntry? entry)
    {
        if (entry is not null)
            _history.Remove(entry);
    }

    [RelayCommand]
    private void TogglePin(ClipEntry? entry)
    {
        if (entry is not null)
            _history.TogglePin(entry);
    }

    /// <summary>
    /// Puts the bind command on the clipboard. Goes through the writer rather than a
    /// toolkit clipboard call so it does not come back as a new history entry.
    /// </summary>
    [RelayCommand]
    private void CopyToggleCommand() => _writer.Set(ClipEntry.FromText(ToggleCommandLine));

    [RelayCommand]
    private void ClearHistory() => _history.Clear();
}
