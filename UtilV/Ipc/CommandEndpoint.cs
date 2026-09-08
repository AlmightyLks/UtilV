using System;
using System.IO;

namespace UtilV.Ipc;

/// <summary>Where the running instance listens for commands.</summary>
internal static class CommandEndpoint
{
    public const string Toggle = "toggle";

    /// <summary>
    /// Unix socket in the user's runtime directory. A socket rather than a FIFO: opening a
    /// FIFO for writing blocks until a reader appears, so a toggle fired while UtilV is
    /// stopped would hang instead of failing.
    /// </summary>
    public static string SocketPath
    {
        get
        {
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");

            if (string.IsNullOrEmpty(runtime) || !Directory.Exists(runtime))
                runtime = Path.GetTempPath();

            return Path.Combine(runtime, "utilv.sock");
        }
    }
}
