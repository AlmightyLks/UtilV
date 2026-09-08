using System;
using System.Text.Json.Serialization;

namespace UtilV.Configuration;

/// <summary>
/// User-configurable limits. Persisted, unlike the history itself, which is deliberately
/// lost on exit.
/// </summary>
internal sealed class UtilVSettings
{
    public const int DefaultMaxHistoryEntries = 25;
    public const int MinHistoryEntries = 5;
    public const int MaxHistoryEntriesLimit = 500;

    public const int DefaultMaxPayloadMegabytes = 16;
    public const int MinPayloadMegabytes = 1;
    public const int MaxPayloadMegabytesLimit = 512;

    /// <summary>Unpinned entries kept. Pinned entries are exempt.</summary>
    public int MaxHistoryEntries { get; set; } = DefaultMaxHistoryEntries;

    /// <summary>Maximum size of a single clipboard entry, in megabytes.</summary>
    public int MaxPayloadMegabytes { get; set; } = DefaultMaxPayloadMegabytes;

    /// <summary>Derived, so it must not be written to the file as a setting of its own.</summary>
    [JsonIgnore]
    public int MaxPayloadBytes => MaxPayloadMegabytes * 1024 * 1024;

    /// <summary>
    /// Brings values into range. The file can be hand-edited, so nothing downstream should
    /// have to defend against a zero or negative cap.
    /// </summary>
    public UtilVSettings Clamped() => new()
    {
        MaxHistoryEntries = Math.Clamp(MaxHistoryEntries, MinHistoryEntries, MaxHistoryEntriesLimit),
        MaxPayloadMegabytes = Math.Clamp(MaxPayloadMegabytes, MinPayloadMegabytes, MaxPayloadMegabytesLimit),
    };
}
