namespace StorageLib.Connectors;

/// <summary>
/// Runtime settings used to expose an rclone remote through loopback SFTP.
/// </summary>
public sealed class RcloneConnectionOptions
{
    public string RemoteName { get; set; } = string.Empty;

    public string ExecutablePath { get; set; } = string.Empty;

    public string ConfigPath { get; set; } = string.Empty;

    public bool IsReadOnly { get; set; }

    public bool DeleteConfigOnDispose { get; set; }

    public int StartupTimeoutSeconds { get; set; } = 30;
}
