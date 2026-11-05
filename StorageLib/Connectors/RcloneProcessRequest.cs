namespace StorageLib.Connectors;

/// <summary>
/// Everything needed to start one rclone SFTP server.
/// </summary>
/// <param name="Options">The connection settings, including how writes are handled.</param>
/// <param name="Target">The remote and subpath to serve, as <c>remote:path</c>.</param>
/// <param name="Sftp">Where the server listens and the credentials it expects.</param>
/// <param name="RemoteControl">
/// Where rclone's control interface listens, or null to start without one. Only useful where rclone
/// buffers writes, because then there is something to ask about.
/// </param>
internal sealed record RcloneProcessRequest(
    RcloneConnectionOptions Options,
    string Target,
    RcloneEndpoint Sftp,
    RcloneEndpoint? RemoteControl);
