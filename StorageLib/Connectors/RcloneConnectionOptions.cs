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

    /// <summary>
    /// Whether rclone buffers writes on local disk before uploading them (<c>--vfs-cache-mode writes</c>).
    /// Without it rclone serves in mode <c>off</c>, where a file has to be written strictly from start to
    /// end and a handle opened for both reading and writing is refused — which is what a filesystem does all
    /// the time, so almost every application would fail to save. Turn it off only for a read-only mount or
    /// where whatever writes through this connection is known to write whole files in order, and closing a
    /// file has to mean the cloud has accepted it.
    /// </summary>
    public bool UseWriteCache { get; set; } = true;

    /// <summary>
    /// Folder rclone keeps its buffered copies in (<c>--cache-dir</c>). Give every connection a folder of
    /// its own and keep it between mounts: rclone finishes uploads left over from a mount that was cut
    /// short the next time it runs against the same folder, and two rclones pointed at one folder resume
    /// and evict each other's half-written uploads. Empty means rclone's own default location, which is
    /// shared by everything.
    /// </summary>
    public string CacheFolder { get; set; } = string.Empty;

    /// <summary>
    /// How long a shutdown waits for rclone to finish the uploads it still has queued, in seconds. Zero
    /// stops immediately and leaves them for the next mount. Only used when writes are buffered. Kept
    /// short enough that unmounting a drive still feels like an answer rather than a hang; what is left
    /// over is not lost, only later.
    /// </summary>
    public int UploadDrainTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// How long rclone waits after the last write before uploading a file, in seconds. A short delay lets a
    /// program that saves in several steps finish before the upload starts.
    /// </summary>
    public int WriteBackSeconds { get; set; } = 5;

    /// <summary>
    /// Upper bound on rclone's own write buffer, in megabytes. This is rclone's staging area for files on
    /// their way up and is separate from FolderMagic's content cache.
    /// </summary>
    public int WriteCacheSizeMegabytes { get; set; } = 4096;

    /// <summary>
    /// How long rclone keeps a buffered copy after it has been uploaded, in hours.
    /// </summary>
    public int WriteCacheMaxAgeHours { get; set; } = 1;
}
