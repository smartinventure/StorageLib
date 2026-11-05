using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;

namespace StorageLib.Connectors;

/// <summary>
/// A loopback port with the throwaway credentials that guard it. One rclone process is reached through
/// two of these: the SFTP server the storage talks to, and the control interface the shutdown asks about
/// pending uploads.
/// </summary>
internal sealed record RcloneEndpoint(int Port, string Username, string Password)
{
    public static RcloneEndpoint CreateOnFreePort() =>
        new(ReserveLoopbackPort(), CreateCredential(), CreateCredential());

    private static int ReserveLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private static string CreateCredential() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
}
