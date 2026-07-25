using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using StorageLib.Abstractions;

namespace StorageLib.Connectors;

/// <summary>
/// Runs an isolated loopback-only rclone SFTP server and delegates storage operations to SSH.NET.
/// </summary>
public sealed class RcloneStorage : IStorage
{
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private readonly ILogger<RcloneStorage>? _logger;
    private Process? _process;
    private SshStorage? _storage;
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
                int port = ReserveLoopbackPort();
                string username = CreateCredential();
                string password = CreateCredential();
                process = CreateProcess(options, target, port, username, password);
                if (!process.Start())
                {
                    throw new InvalidOperationException("The rclone process could not be started.");
                }

                process.StartInfo.ArgumentList.Clear();
                _process = process;
                _standardOutputDrain = DrainAsync(process.StandardOutput);
                _standardErrorDrain = DrainAsync(process.StandardError);
                _storage = await WaitForSftpAsync(
                    process,
                    port,
                    username,
                    password,
                    TimeSpan.FromSeconds(options.StartupTimeoutSeconds),
                    cancellationToken).ConfigureAwait(false);
                _storage.MaxReadBytesPerSecond = _maxReadBytesPerSecond;
                _storage.MaxWriteBytesPerSecond = _maxWriteBytesPerSecond;
                ConnectionString = connectionString;
                BaseFolderOrContainer = RcloneConfiguration.NormalizeSubpath(baseFolderOrContainer);
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

    private static Process CreateProcess(
        RcloneConnectionOptions options,
        string target,
        int port,
        string username,
        string password)
    {
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
        startInfo.ArgumentList.Add(target);
        startInfo.ArgumentList.Add("--addr");
        startInfo.ArgumentList.Add($"127.0.0.1:{port}");
        startInfo.ArgumentList.Add("--user");
        startInfo.ArgumentList.Add(username);
        startInfo.ArgumentList.Add("--pass");
        startInfo.ArgumentList.Add(password);
        startInfo.ArgumentList.Add("--config");
        startInfo.ArgumentList.Add(options.ConfigPath);
        if (options.IsReadOnly)
        {
            startInfo.ArgumentList.Add("--read-only");
        }

        return new Process { StartInfo = startInfo };
    }

    private static async Task<SshStorage> WaitForSftpAsync(
        Process process,
        int port,
        string username,
        string password,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linkedSource = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        string connectionString =
            $"sftp://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@127.0.0.1:{port}";

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
        bool canDeleteConfig = process is null;
        try
        {
            if (process is not null)
            {
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
            process?.Dispose();
            _standardOutputDrain = null;
            _standardErrorDrain = null;
            ConnectionString = string.Empty;
            BaseFolderOrContainer = string.Empty;
            if (canDeleteConfig)
            {
                DeleteOwnedConfig();
            }
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
