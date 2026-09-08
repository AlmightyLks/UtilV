using System;
using System.Runtime.InteropServices;
using Avalonia;
using UtilV.Ipc;

namespace UtilV;

internal sealed class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // Handled before Avalonia starts, so a toggle does not pay for UI initialisation
        // it is about to throw away.
        if (Array.Exists(args, a => a is "--toggle" or "-t"))
        {
            if (CommandClient.TrySend(CommandEndpoint.Toggle))
                return 0;

            Console.Error.WriteLine("UtilV is not running.");
            return 1;
        }

        if (Array.Exists(args, a => a is "--help" or "-h"))
        {
            PrintUsage();
            return 0;
        }

        // Two daemons would fight over selection ownership, so a second launch defers to
        // the first rather than racing it.
        if (CommandClient.TrySend(CommandEndpoint.Toggle))
        {
            Console.Error.WriteLine("UtilV is already running; toggled the existing instance.");
            return 0;
        }

        LeaveXSessionManagement();

        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Stops the toolkit from joining the desktop's X session management.
    ///
    /// Avalonia's X11 backend opens an XSMP connection when SESSION_MANAGER is set, which
    /// registers UtilV as a session client. At logout or shutdown the session manager then
    /// waits for it to answer the end-session query, and a background service that never
    /// answers leaves the session manager waiting instead of powering off. There is no
    /// session state here worth saving: the history is deliberately not persisted, and
    /// systemd stops the service through graphical-session.target.
    ///
    /// Cleared here rather than only in the systemd unit, so it also covers being run by
    /// hand or from an IDE, where the variable is inherited from the session.
    ///
    /// unsetenv, not Environment.SetEnvironmentVariable: on Unix the managed call only
    /// updates .NET's own copy of the environment, and the SMLib code reads it through
    /// getenv, which would still see the inherited value.
    /// </summary>
    private static void LeaveXSessionManagement()
    {
        try
        {
            unsetenv("SESSION_MANAGER");
        }
        catch (DllNotFoundException)
        {
            // Not Linux, or no libc to call. Session management is an X11 concern only.
        }
    }

    [DllImport("libc", SetLastError = true)]
    private static extern int unsetenv(string name);

    // Also used by the XAML designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();

    private static void PrintUsage()
    {
        var exe = Environment.ProcessPath ?? "utilv";

        Console.WriteLine($"""
            UtilV - clipboard history

              {exe}            run the daemon
              {exe} --toggle   show or hide the clipboard popup

            Alt+V opens the popup and needs no setting up. If another program already
            holds that combination, bind a key to this command instead:

              {exe} --toggle
            """);
    }
}
