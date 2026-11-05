using System.Diagnostics;
using Microsoft.Extensions.Logging;
using StorageLib.Abstractions;

namespace StorageLib.Connectors;

/// <summary>
/// Runs an isolated loopback-only rclone SFTP server and delegates storage operations to SSH.NET.
/// </summary>
public sealed class RcloneStorage : IStorage, IPendingUploadSource
{
    private const int DrainPollMilliseconds = 250;
    private const int DrainUnreachableAttempts = 3;

    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ILogger<RcloneStorage>? _logger;
    private Process? _process;
    private SshStorage? _storage;
    private RcloneRemoteControl? _remoteControl;
    private string _servedTarget = string.Empty;
    private int _uploadDrainTimeoutSeconds;
    private Task? _standardOutputDrain;
    private Task? _standardErrorDrain;
    private string? _ownedConfigPath;
    private long _maxReadBytesPerSecond;
    private long _maxWriteBytesPerSecond;
    private bool _isDisposed;

    public RcloneStorage(ILogger<RcloneStorage>? logger = null) => _logger = logger;

    public EnumStorageType Type { get => EnumStorageType.Rclone; set { } }

    public string BaseFolderOrContainer { get; set; } = string.Empty;

    public string ConnectionString { get; set; } = string.Empty;

    public long MaxReadBytesPerSecond
    {
        get => _maxReadBytesPerSecond;
        set
        {
            _maxReadBytesPerSecond = value;
            if (_storage is not null)
            {
                _storage.MaxReadBytesPerSecond = value;
            }
        }
    }

    public long MaxWriteBytesPerSecond
    {
        get => _maxWriteBytesPerSecond;
        set
        {
            _maxWriteBytesPerSecond = value;
            if (_storage is not null)
            {
                _storage.MaxWriteBytesPerSecond = value;
            }
        }
    }

    public async Task InitializeAsync(
        string connectionString,
        string baseFolderOrContainer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ShutdownCoreAsync(CancellationToken.None).ConfigureAwait(false);
            Process? process = null;
            try
            {
                RcloneConnectionOptions options = RcloneConfiguration.Parse(connectionString);
                if (options.DeleteConfigOnDispose &&
                    RcloneConfiguration.IsOwnableConfigPath(options.ConfigPath))
                {
                    _ownedConfigPath = options.ConfigPath;
                }

                RcloneConfiguration.Validate(options);
                string target = BuildTarget(options.RemoteName, baseFolderOrContainer);
                var sftp = RcloneEndpoint.CreateOnFreePort();
                // The control interface is only worth having where rclone holds files back: it is how the
                // shutdown finds out whether anything is still waiting to go up.
                RcloneEndpoint? remoteControl = !options.IsReadOnly && options.UseWriteCache
                    ? RcloneEndpoint.CreateOnFreePort()
                    : null;
                process = CreateProcess(new RcloneProcessRequest(options, target, sftp, remoteControl));
                if (!process.Start())
                {
                    throw new InvalidOperationException("The rclone process could not be started.");
                }

                process.StartInfo.ArgumentList.Clear();
                _process = process;
                _uploadDrainTimeoutSeconds = options.UploadDrainTimeoutSeconds;
                _remoteControl = remoteControl is null
                    ? null
                    : new RcloneRemoteControl(remoteControl.Port, remoteControl.Username, remoteControl.Password);
                _standardOutputDrain = DrainAsync(process.StandardOutput);
                _standardErrorDrain = DrainAsync(process.StandardError);
                _storage = await WaitForSftpAsync(
                    process,
                    sftp,
                    TimeSpan.FromSeconds(options.StartupTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);
                _storage.MaxReadBytesPerSecond = _maxReadBytesPerSecond;
                _storage.MaxWriteBytesPerSecond = _maxWriteBytesPerSecond;
                ConnectionString = connectionString;
                BaseFolderOrContainer = RcloneConfiguration.NormalizeSubpath(baseFolderOrContainer);
                _servedTarget = target;
                _logger?.LogInformation(
                    "Rclone SFTP connector started for remote {RemoteName} on loopback",
                    options.RemoteName);
            }
            catch
            {
                if (process is not null)
                {
                    process.StartInfo.ArgumentList.Clear();
                    if (_process is null)
                    {
                        process.Dispose();
                    }
                }

                await ShutdownCoreAsync(CancellationToken.None).ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task<IntPtr> OpenAsync(string realPath, OpenFlags flags, CancellationToken cancellationToken = default) =>
        GetStorage().OpenAsync(realPath, flags, cancellationToken);

    public Task CloseAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default) =>
        GetStorage().CloseAsync(path, fileHandle, cancellationToken);

    public Task<int> ReadAsync(
        string path,
        IntPtr fileHandle,
        long offset,
        long size,
        IntPtr buffer,
        CancellationToken cancellationToken = default) =>
        GetStorage().ReadAsync(path, fileHandle, offset, size, buffer, cancellationToken);

    public Task<int> WriteAsync(
        string path,
        IntPtr fileHandle,
        long offset,
        long size,
        IntPtr buffer,
        CancellationToken cancellationToken = default) =>
        GetStorage().WriteAsync(path, fileHandle, offset, size, buffer, cancellationToken);

    public Task<IEnumerable<FileObject>> ReadDirAsync(
        string realPath,
        bool readOnly = true,
        CancellationToken cancellationToken = default) =>
        GetStorage().ReadDirAsync(realPath, readOnly, cancellationToken);

    public Task<bool> TestReadAsync(CancellationToken cancellationToken = default) =>
        GetStorage().TestReadAsync(cancellationToken);

    public Task<bool> TestWriteAsync(CancellationToken cancellationToken = default) =>
        GetStorage().TestWriteAsync(cancellationToken);

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default) =>
        GetStorage().FileExistsAsync(filePath, cancellationToken);

    public Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default) =>
        GetStorage().DirectoryExistsAsync(directoryPath, cancellationToken);

    public Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default) =>
        GetStorage().DeleteDirectoryAsync(path, cancellationToken);

    public Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default) =>
        GetStorage().GetFileInfoAsync(filePath, cancellationToken);

    public Task DeleteAsync(string filePath, CancellationToken cancellationToken = default) =>
        GetStorage().DeleteAsync(filePath, cancellationToken);

    public Task MoveAsync(
        string sourceFilePath,
        string destinationFilePath,
        bool overwrite,
        CancellationToken cancellationToken = default) =>
        GetStorage().MoveAsync(sourceFilePath, destinationFilePath, overwrite, cancellationToken);

    public Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) =>
        GetStorage().CreateDirectoryAsync(directoryPath, cancellationToken);

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await ShutdownCoreAsync(CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    public Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default) =>
        GetStorage().GetTotalCapacityAsync(cancellationToken);

    public Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default) =>
        GetStorage().GetAvailableFreeSpaceAsync(cancellationToken);

    public Task CloseAllHandlesAsync(CancellationToken cancellationToken = default) =>
        GetStorage().CloseAllHandlesAsync(cancellationToken);

    public Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default) =>
        GetStorage().ChownAsync(path, uid, gid, cancellationToken);

    public Task SetTimesAsync(
        string path,
        DateTime accessTime,
        DateTime modifiedTime,
        CancellationToken cancellationToken = default) =>
        GetStorage().SetTimesAsync(path, accessTime, modifiedTime, cancellationToken);

    public Task FlushAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default) =>
        GetStorage().FlushAsync(path, fileHandle, cancellationToken);

    public Task FAllocateAsync(
        string path,
        IntPtr fileHandle,
        int mode,
        long offset,
        long length,
        CancellationToken cancellationToken = default) =>
        GetStorage().FAllocateAsync(path, fileHandle, mode, offset, length, cancellationToken);

    public Task TruncateAsync(
        string path,
        IntPtr fileHandle,
        long size,
        CancellationToken cancellationToken = default) =>
        GetStorage().TruncateAsync(path, fileHandle, size, cancellationToken);

    public static string BuildTarget(string remoteName, string? subpath)
        => RcloneConfiguration.BuildTarget(remoteName, subpath);

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        try
        {
            ShutdownAsync().GetAwaiter().GetResult();
        }
        finally
        {
            _isDisposed = true;
            _lifecycleLock.Dispose();
        }
    }

    internal static Process CreateProcess(RcloneProcessRequest request)
    {
        RcloneConnectionOptions options = request.Options;
        var startInfo = new ProcessStartInfo
        {
            FileName = options.ExecutablePath,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        startInfo.ArgumentList.Add("serve");
        startInfo.ArgumentList.Add("sftp");
        startInfo.ArgumentList.Add(request.Target);
        startInfo.ArgumentList.Add("--addr");
        startInfo.ArgumentList.Add($"127.0.0.1:{request.Sftp.Port}");
        startInfo.ArgumentList.Add("--user");
        startInfo.ArgumentList.Add(request.Sftp.Username);
        startInfo.ArgumentList.Add("--pass");
        startInfo.ArgumentList.Add(request.Sftp.Password);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(options.ConfigPath);
        if (options.CacheFolder.Length > 0)
        {
            startInfo.ArgumentList.Add("--cache-dir");
            startInfo.ArgumentList.Add(options.CacheFolder);
        }

        if (options.IsReadOnly)
        {
            startInfo.ArgumentList.Add("--read-only");
        }
        else if (options.UseWriteCache)
        {
            // "writes" buffers the file locally and uploads it on close, so a program may save in any
            // order and rclone gets to retry a flaky connection. The cost is that a finished save means
            // "on this computer"; the control interface below is what lets us wait for the rest.
            startInfo.ArgumentList.Add("--vfs-cache-mode");
            startInfo.ArgumentList.Add("writes");
            startInfo.ArgumentList.Add("--vfs-write-back");
            startInfo.ArgumentList.Add($"{Math.Max(0, options.WriteBackSeconds)}s");
            startInfo.ArgumentList.Add("--vfs-cache-max-size");
            startInfo.ArgumentList.Add($"{Math.Max(1, options.WriteCacheSizeMegabytes)}M");
            startInfo.ArgumentList.Add("--vfs-cache-max-age");
            startInfo.ArgumentList.Add($"{Math.Max(1, options.WriteCacheMaxAgeHours)}h");
        }
        else
        {
            // Without a buffer rclone serves in mode "off": every write goes to the remote as it arrives
            // and closing the file is what commits and verifies it. Nothing can be left behind, but a file
            // has to be written from start to end and cannot be opened for reading and writing at once.
            startInfo.ArgumentList.Add("--vfs-cache-mode");
            startInfo.ArgumentList.Add("off");
        }

        if (request.RemoteControl is not null)
        {
            startInfo.ArgumentList.Add("--rc");
            startInfo.ArgumentList.Add("--rc-addr");
            startInfo.ArgumentList.Add($"127.0.0.1:{request.RemoteControl.Port}");
            // Credentials go through the environment, which other users cannot read, unlike a command line.
            startInfo.Environment["RCLONE_RC_USER"] = request.RemoteControl.Username;
            startInfo.Environment["RCLONE_RC_PASS"] = request.RemoteControl.Password;
        }

        return new Process { StartInfo = startInfo };
    }

    private static async Task<SshStorage> WaitForSftpAsync(
        Process process,
        RcloneEndpoint endpoint,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        string connectionString =
            $"sftp://{Uri.EscapeDataString(endpoint.Username)}:" +
            $"{Uri.EscapeDataString(endpoint.Password)}@127.0.0.1:{endpoint.Port}";

        while (true)
        {
            linkedSource.Token.ThrowIfCancellationRequested();
            if (process.HasExited)
            {
                throw new InvalidOperationException($"Rclone exited before SFTP startup (exit code {process.ExitCode}).");
            }

            var storage = new SshStorage();
            try
            {
                await storage.InitializeAsync(connectionString, "/", linkedSource.Token).ConfigureAwait(false);
                return storage;
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException &&
                exception is not ObjectDisposedException)
            {
                storage.Dispose();
                await Task.Delay(TimeSpan.FromMilliseconds(100), linkedSource.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// What rclone still has to upload for this connection. Zero while a read-only connection or one that
    /// writes straight through is in use, because neither holds anything back.
    /// </summary>
    public async Task<PendingUploadState> GetPendingUploadsAsync(CancellationToken cancellationToken = default)
    {
        RcloneRemoteControl? remoteControl = _remoteControl;
        if (remoteControl is null || _process is null or { HasExited: true })
        {
            return PendingUploadState.None;
        }

        return await remoteControl.GetPendingUploadsAsync(cancellationToken).ConfigureAwait(false)
               ?? PendingUploadState.None;
    }

    public string PendingUploadSourceName =>
        _servedTarget.Length == 0 ? "cloud storage" : $"cloud storage {_servedTarget}";

    private async Task ShutdownCoreAsync(CancellationToken cancellationToken)
    {
        SshStorage? storage = Interlocked.Exchange(ref _storage, null);
        if (storage is not null)
        {
            try
            {
                await storage.ShutdownAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger?.LogWarning(exception, "Could not shut down the delegated SFTP storage cleanly");
            }
            finally
            {
                try
                {
                    storage.Dispose();
                }
                catch (Exception exception)
                {
                    _logger?.LogWarning(exception, "Could not dispose the delegated SFTP storage cleanly");
                }
            }
        }

        Process? process = Interlocked.Exchange(ref _process, null);
        RcloneRemoteControl? remoteControl = Interlocked.Exchange(ref _remoteControl, null);
        bool canDeleteConfig = process is null;
        try
        {
            if (process is not null)
            {
                if (remoteControl is not null)
                {
                    await WaitForPendingUploadsAsync(process, remoteControl).ConfigureAwait(false);
                }

                if (!process.HasExited)
                {
                    process.Kill(true);
                }

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
                canDeleteConfig = true;
                await AwaitDrainAsync(_standardOutputDrain).ConfigureAwait(false);
                await AwaitDrainAsync(_standardErrorDrain).ConfigureAwait(false);
            }
        }
        finally
        {
            remoteControl?.Dispose();
            process?.Dispose();
            _standardOutputDrain = null;
            _standardErrorDrain = null;
            _servedTarget = string.Empty;
            ConnectionString = string.Empty;
            BaseFolderOrContainer = string.Empty;
            if (canDeleteConfig)
            {
                DeleteOwnedConfig();
            }
        }
    }

    /// <summary>
    /// Waits for rclone to finish the uploads it is holding before the process is stopped. Killing it
    /// with files still queued is not data loss — the cache folder is kept and rclone picks them up the
    /// next time the same remote is mounted — but it does mean a file the user saved is not in the cloud
    /// yet, so it is worth waiting for and worth a log entry when the wait runs out.
    /// </summary>
    private async Task WaitForPendingUploadsAsync(Process process, RcloneRemoteControl remoteControl)
    {
        if (_uploadDrainTimeoutSeconds <= 0)
        {
            return;
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(_uploadDrainTimeoutSeconds);
        int unreachableAttempts = 0;
        while (!process.HasExited)
        {
            PendingUploadState? pending = await remoteControl
                .GetPendingUploadsAsync(CancellationToken.None)
                .ConfigureAwait(false);
            if (pending is null)
            {
                // Asking is the only way to know, so a control interface that does not answer means
                // waiting would be waiting blind. Give it a few tries, then stop.
                if (++unreachableAttempts >= DrainUnreachableAttempts)
                {
                    _logger?.LogWarning(
                        "Rclone did not report its upload queue for {RemoteTarget}; stopping without waiting",
                        _servedTarget);
                    return;
                }
            }
            else
            {
                unreachableAttempts = 0;
                if (!pending.HasPendingUploads)
                {
                    if (pending.FailedFileCount > 0)
                    {
                        _logger?.LogWarning(
                            "Rclone reports {FailedFileCount} file(s) it could not upload to {RemoteTarget}",
                            pending.FailedFileCount,
                            _servedTarget);
                    }

                    return;
                }

                if (DateTime.UtcNow >= deadline)
                {
                    _logger?.LogWarning(
                        "Stopping rclone for {RemoteTarget} with {FileCount} upload(s) still queued; " +
                        "they stay in the cache folder and are retried on the next mount",
                        _servedTarget,
                        pending.FileCount);
                    return;
                }
            }

            if (DateTime.UtcNow >= deadline)
            {
                return;
            }

            await Task.Delay(DrainPollMilliseconds, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private void DeleteOwnedConfig()
    {
        string? configPath = Interlocked.Exchange(ref _ownedConfigPath, null);
        if (configPath is null)
        {
            return;
        }

        try
        {
            File.Delete(configPath);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Interlocked.CompareExchange(ref _ownedConfigPath, configPath, null);
            _logger?.LogWarning(exception, "Could not delete the owned temporary rclone configuration");
        }
    }

    private static async Task AwaitDrainAsync(Task? drainTask)
    {
        if (drainTask is not null)
        {
            await drainTask.ConfigureAwait(false);
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync(CancellationToken.None).ConfigureAwait(false) is not null)
        {
        }
    }

    private SshStorage GetStorage()
    {
        ThrowIfDisposed();
        return _storage ?? throw new InvalidOperationException("Rclone storage is not initialized.");
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}
