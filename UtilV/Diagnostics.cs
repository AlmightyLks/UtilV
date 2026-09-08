using System;
using System.IO;

namespace UtilV;

/// <summary>
/// Opt-in tracing, a no-op unless UTILV_DIAG names a file to append to. A file rather than
/// stderr, so it still works when launched from a desktop shortcut:
///
///     UTILV_DIAG=/tmp/utilv.log utilv
/// </summary>
internal static class Diagnostics
{
    private static readonly string? LogPath = ResolvePath();
    private static readonly object Gate = new();

    public static void Log(string message)
    {
        if (LogPath is null)
            return;

        lock (Gate)
        {
            try
            {
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId,3}] {message}\n");
            }
            catch (IOException)
            {
                // Tracing must never take the app down.
            }
        }
    }

    private static string? ResolvePath()
    {
        var configured = Environment.GetEnvironmentVariable("UTILV_DIAG");

        if (string.IsNullOrEmpty(configured))
            return null;

        try
        {
            var directory = Path.GetDirectoryName(configured);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);
        }
        catch (IOException)
        {
            return null;
        }

        return configured;
    }
}
