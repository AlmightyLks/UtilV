using System;
using System.Net.Sockets;
using System.Text;

namespace UtilV.Ipc;

/// <summary>Sends a command to an already-running UtilV, if there is one.</summary>
internal static class CommandClient
{
    /// <summary>
    /// Returns false when no instance is listening, which also serves as the
    /// single-instance check: a failed connect means this process should become
    /// the daemon.
    /// </summary>
    public static bool TrySend(string command, int timeoutMs = 1000)
    {
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.SendTimeout = timeoutMs;

            var connected = socket.BeginConnect(new UnixDomainSocketEndPoint(CommandEndpoint.SocketPath), null, null);

            if (!connected.AsyncWaitHandle.WaitOne(timeoutMs))
                return false;

            socket.EndConnect(connected);
            socket.Send(Encoding.UTF8.GetBytes(command + "\n"));
            return true;
        }
        catch (SocketException)
        {
            return false;   // nobody home, or a stale socket file
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return false;
        }
    }
}
