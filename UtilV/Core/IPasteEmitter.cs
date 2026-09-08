using System;

namespace UtilV.Core;

/// <summary>Returns focus to a window and synthesises a paste into it.</summary>
internal interface IPasteEmitter
{
    void PasteInto(IntPtr window);
}
