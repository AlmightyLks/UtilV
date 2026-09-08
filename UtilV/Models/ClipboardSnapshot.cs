namespace UtilV.Models;

/// <summary>What the monitor read from the X selection, before it becomes an entry.</summary>
internal sealed record ClipboardSnapshot(ClipKind Kind, string? Text, byte[]? ImageBytes)
{
    public static ClipboardSnapshot ForText(string text) => new(ClipKind.Text, text, null);

    public static ClipboardSnapshot ForImage(byte[] png) => new(ClipKind.Image, null, png);

    public ClipEntry ToEntry() =>
        Kind == ClipKind.Image
            ? ClipEntry.FromImage(ImageBytes!)
            : ClipEntry.FromText(Text!);
}
