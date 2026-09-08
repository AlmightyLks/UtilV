using UtilV.Models;

namespace UtilV.Core;

/// <summary>
/// Puts an entry on the clipboard. On X11 that is an ongoing obligation rather than a
/// write: the process becomes the selection owner and must keep serving requests for as
/// long as the entry is current.
/// </summary>
internal interface IClipboardWriter
{
    void Set(ClipEntry entry);
}
