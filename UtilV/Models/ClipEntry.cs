using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Media.Imaging;

namespace UtilV.Models;

/// <summary>
/// One captured clipboard item. Raises change notifications because entries are bound
/// directly by the list.
/// </summary>
internal sealed class ClipEntry : INotifyPropertyChanged
{
    public const int ThumbnailWidth = 260;

    /// <summary>How much of a text entry the list shows before it is cut off.</summary>
    private const int PreviewLines = 3;
    private const int PreviewLineLength = 120;

    private bool _isPinned;
    private Bitmap? _thumbnail;
    private bool _thumbnailAttempted;

    public event PropertyChangedEventHandler? PropertyChanged;

    private ClipEntry(ClipKind kind, string hash)
    {
        Kind = kind;
        Hash = hash;
        CapturedAt = DateTimeOffset.Now;
    }

    public static ClipEntry FromText(string text) =>
        new(ClipKind.Text, ContentKey(ClipKind.Text, Encoding.UTF8.GetBytes(text))) { Text = text };

    public static ClipEntry FromImage(byte[] png) =>
        new(ClipKind.Image, ContentKey(ClipKind.Image, png)) { ImageBytes = png };

    public ClipKind Kind { get; }

    /// <summary>Content hash, so dedup is a lookup rather than a scan.</summary>
    public string Hash { get; }

    public string? Text { get; private init; }

    public byte[]? ImageBytes { get; private init; }

    /// <summary>Refreshed when an existing entry is promoted back to the top.</summary>
    public DateTimeOffset CapturedAt { get; set; }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value)
                return;

            _isPinned = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
        }
    }

    public bool HasThumbnail => Kind == ClipKind.Image;

    /// <summary>
    /// Summary for the list. Keeps the first few line breaks, so a multi-line copy is
    /// recognisable as one instead of looking like whatever its first line happened to be.
    /// </summary>
    public string Preview
    {
        get
        {
            if (Kind == ClipKind.Image)
                return $"Image ({FormatSize(ImageBytes?.Length ?? 0)})";

            var text = Text ?? string.Empty;
            var body = text.ReplaceLineEndings("\n").Trim();

            if (body.Length == 0)
                return $"{text.Length} whitespace characters";

            var lines = body.Split('\n');
            var kept = new string[Math.Min(lines.Length, PreviewLines)];

            for (int i = 0; i < kept.Length; i++)
                kept[i] = Shorten(lines[i].TrimEnd());

            // Marks the lines that did not fit, rather than ending on an arbitrary one.
            if (lines.Length > kept.Length)
                kept[^1] += " ...";

            return string.Join('\n', kept);
        }
    }

    /// <summary>
    /// Decoded lazily and cached: only image rows need it, and decoding on capture would
    /// pay for entries the user never scrolls to. Kept out of the constructor so the model
    /// stays constructible without an initialised Avalonia platform.
    /// </summary>
    public Bitmap? Thumbnail
    {
        get
        {
            if (_thumbnailAttempted || Kind != ClipKind.Image || ImageBytes is null)
                return _thumbnail;

            _thumbnailAttempted = true;

            try
            {
                using var stream = new MemoryStream(ImageBytes);
                _thumbnail = Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            }
            catch (Exception)
            {
                // Some applications advertise image/png and serve something else.
                _thumbnail = null;
            }

            return _thumbnail;
        }
    }

    /// <summary>
    /// Cuts an over-long line. Backs off a character when the cut would land between the
    /// halves of a surrogate pair, which would otherwise leave a broken glyph before the
    /// ellipsis.
    /// </summary>
    private static string Shorten(string line)
    {
        if (line.Length <= PreviewLineLength)
            return line;

        int cut = PreviewLineLength;

        if (char.IsHighSurrogate(line[cut - 1]))
            cut--;

        return line[..cut] + "...";
    }

    /// <summary>
    /// The kind is part of the key: text and an image can hold identical bytes, and
    /// hashing content alone would let one swallow the other during dedup.
    /// </summary>
    private static string ContentKey(ClipKind kind, byte[] bytes) =>
        $"{kind}:{Convert.ToHexString(SHA256.HashData(bytes))}";

    private static string FormatSize(int bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => (bytes / (1024.0 * 1024.0)).ToString("0.#", CultureInfo.InvariantCulture) + " MB",
    };
}
