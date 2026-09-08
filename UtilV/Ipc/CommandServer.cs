using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace UtilV.Ipc;

/// <summary>
/// Accepts commands from `utilv --toggle` on a Unix socket.
///
/// Binding an actual key combination is left outside the app: Linux has no portable
/// global-hotkey API, and on Wayland none is possible. Anything that can run a command
/// can drive this instead.
/// </summary>
internal sealed class CommandServer : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private Socket? _listener;
    private Thread? _thread;

    public event EventHandler<string>? CommandReceived;

    public bool Start()
    {
        var path = CommandEndpoint.SocketPath;

        // A leftover socket file from a crash would block bind(). Connecting to it is the
        // only reliable liveness test, and the caller has already done that.
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException ex)
        {
            Diagnostics.Log($"CommandServer: cannot remove stale socket: {ex.Message}");
            return false;
        }

        try
        {
            _listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _listener.Bind(new UnixDomainSocketEndPoint(path));
            _listener.Listen(4);
        }
        catch (SocketException ex)
        {
            Diagnostics.Log($"CommandServer: bind failed: {ex.Message}");
            return false;
        }

        _thread = new Thread(Run) { IsBackground = true, Name = "UtilV command socket" };
        _thread.Start();

        Diagnostics.Log($"CommandServer: listening on {path}");
        return true;
    }

    private void Run()
    {
        var buffer = new byte[256];

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                using var client = _listener!.Accept();
                int read = client.Receive(buffer);

                if (read <= 0)
                    continue;

                foreach (var line in Encoding.UTF8.GetString(buffer, 0, read)
                             .Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var command = line.Trim();

                    if (command.Length == 0)
                        continue;

                    Diagnostics.Log($"CommandServer: received '{command}'");
                    CommandReceived?.Invoke(this, command);
                }
            }
            catch (Exception ex) when (!_cts.IsCancellationRequested)
            {
                Diagnostics.Log($"CommandServer: {ex.GetType().Name}, continuing");
                Thread.Sleep(100);
            }
            catch (Exception)
            {
                return;   // shutting down
            }
        }
    }

    public void Dispose()
    {
        _cts.Cancel();

        try
        {
            _listener?.Close();   // unblocks Accept
            File.Delete(CommandEndpoint.SocketPath);
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException)
        {
            // Best effort during shutdown.
        }

        _thread?.Join(TimeSpan.FromSeconds(1));
        _cts.Dispose();
    }
}
