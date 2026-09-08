using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using UtilV.Configuration;
using UtilV.Models;

namespace UtilV.Core;

/// <summary>
/// Ordered clipboard history: pinned entries first, then unpinned, each newest first.
///
/// Not thread-safe. The collection is bound to the popup, so callers must marshal onto
/// the UI thread before touching it.
/// </summary>
internal sealed class ClipHistory
{
    private readonly ObservableCollection<ClipEntry> _entries = [];
    private readonly Dictionary<string, ClipEntry> _byHash = [];

    private int _maxUnpinned = UtilVSettings.DefaultMaxHistoryEntries;

    public ClipHistory()
    {
        Entries = new ReadOnlyObservableCollection<ClipEntry>(_entries);
    }

    public ReadOnlyObservableCollection<ClipEntry> Entries { get; }

    /// <summary>
    /// How many unpinned entries to keep. Pinned entries are exempt. Lowering it trims
    /// immediately rather than waiting for the next capture.
    /// </summary>
    public int MaxUnpinned
    {
        get => _maxUnpinned;
        set
        {
            _maxUnpinned = Math.Max(1, value);
            Trim();
        }
    }

    /// <summary>
    /// Records a capture. Content already present anywhere in the list is promoted rather
    /// than duplicated, keeping its pinned state, so re-copying a pinned entry does not
    /// silently unpin it.
    /// </summary>
    public ClipEntry Add(ClipboardSnapshot snapshot)
    {
        var candidate = snapshot.ToEntry();

        if (_byHash.TryGetValue(candidate.Hash, out var existing))
        {
            existing.CapturedAt = DateTimeOffset.Now;
            Reposition(existing);
            return existing;
        }

        _byHash[candidate.Hash] = candidate;
        _entries.Insert(PinnedCount, candidate);
        Trim();
        return candidate;
    }

    /// <summary>
    /// Moves an entry back to the top of its section, as if it had just been copied. Used
    /// when the user picks an entry, since putting it on the clipboard does not come back
    /// through the monitor.
    /// </summary>
    public void Promote(ClipEntry entry)
    {
        if (!_entries.Contains(entry))
            return;

        entry.CapturedAt = DateTimeOffset.Now;
        Reposition(entry);
    }

    public void Remove(ClipEntry entry)
    {
        if (_entries.Remove(entry))
            _byHash.Remove(entry.Hash);
    }

    /// <summary>
    /// Pinning lifts an entry to the top and holds it there; unpinning drops it back into
    /// the unpinned block, where it is subject to the cap again.
    /// </summary>
    public void TogglePin(ClipEntry entry)
    {
        if (!_entries.Contains(entry))
            return;

        entry.IsPinned = !entry.IsPinned;
        Reposition(entry);

        if (!entry.IsPinned)
            Trim();
    }

    public void Clear()
    {
        _entries.Clear();
        _byHash.Clear();
    }

    /// <summary>Pinned entries always occupy the head of the list.</summary>
    private int PinnedCount
    {
        get
        {
            int count = 0;

            while (count < _entries.Count && _entries[count].IsPinned)
                count++;

            return count;
        }
    }

    /// <summary>
    /// Re-files an entry. Pinned entries go to the very top; unpinned ones slot in by
    /// capture time within the unpinned block, which matters when unpinning: an old entry
    /// placed at the head would look like the newest capture and outlive newer ones at
    /// trim time.
    /// </summary>
    private void Reposition(ClipEntry entry)
    {
        _entries.Remove(entry);

        if (entry.IsPinned)
        {
            _entries.Insert(0, entry);
            return;
        }

        int index = PinnedCount;

        while (index < _entries.Count && _entries[index].CapturedAt > entry.CapturedAt)
            index++;

        _entries.Insert(index, entry);
    }

    /// <summary>Drops the oldest unpinned entries once there are more than the cap allows.</summary>
    private void Trim()
    {
        var unpinned = _entries.Where(e => !e.IsPinned).ToList();

        for (int i = unpinned.Count - 1; i >= _maxUnpinned; i--)
            Remove(unpinned[i]);
    }
}
