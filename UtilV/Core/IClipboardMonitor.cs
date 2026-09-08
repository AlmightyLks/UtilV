using System;
using UtilV.Models;

namespace UtilV.Core;

/// <summary>Reports clipboard changes made by other applications.</summary>
internal interface IClipboardMonitor
{
    /// <summary>Raised off the UI thread when another application sets the clipboard.</summary>
    event EventHandler<ClipboardSnapshot>? Captured;

    void Start();
}
