using Renci.SshNet;

namespace StorageLib.Connectors;

internal static class SshConnectionFactory
{
    public static SftpClient Create(string connectionString)
    {
        Uri uri = ParseConnectionString(connectionString);
        string[] userInfo = uri.UserInfo.Split(':', 2);
        string username = Uri.UnescapeDataString(userInfo[0]);
        Dictionary<string, string> query = ParseQuery(uri.Query);
        if (query.TryGetValue("keyFile", out string? keyFile))
        {
            if (!File.Exists(keyFile))
            {
                throw new FileNotFoundException("The SSH private key file does not exist.", keyFile);
            }

            query.TryGetValue("passphrase", out string? passphrase);
            var privateKey = new PrivateKeyFile(keyFile, passphrase);
            return new SftpClient(uri.Host, uri.Port > 0 ? uri.Port : 22, username, privateKey);
        }

        if (userInfo.Length != 2)
        {
            throw new ArgumentException("The SSH connection string must contain a password or keyFile.");
        }

        return new SftpClient(
            uri.Host,
            uri.Port > 0 ? uri.Port : 22,
            username,
            Uri.UnescapeDataString(userInfo[1]));
    }

    private static Uri ParseConnectionString(string connectionString)
    {
        if (!Uri.TryCreate(connectionString, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme is not ("ssh" or "sftp") ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            string.IsNullOrWhiteSpace(uri.UserInfo))
        {
            throw new ArgumentException("A valid ssh:// or sftp:// connection string is required.", nameof(connectionString));
        }

        return uri;
    }

    private static Dictionary<string, string> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .ToDictionary(
                part => Uri.UnescapeDataString(part[0]),
                part => part.Length == 2 ? Uri.UnescapeDataString(part[1]) : string.Empty,
                StringComparer.OrdinalIgnoreCase);
}
