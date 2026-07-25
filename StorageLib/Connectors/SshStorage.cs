using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Renci.SshNet;
using StorageLib.Abstractions;

namespace StorageLib.Connectors;

/// <summary>
/// SFTP storage backed by SSH.NET.
/// </summary>
public sealed class SshStorage : IStorage
{
    private readonly ConcurrentDictionary<IntPtr, SshFileHandle> _handles = new();
    private readonly SemaphoreSlim _clientLock = new(1, 1);
    private readonly ILogger<SshStorage>? _logger;
    private SftpClient? _client;
    private long _nextHandle;
    private bool _isDisposed;
    private string _baseFolder = "/";
    public SshStorage(ILogger<SshStorage>? logger = null) => _logger = logger;
    public EnumStorageType Type { get => EnumStorageType.SSH; set { } }
    public string BaseFolderOrContainer { get => _baseFolder; set => _baseFolder = SshPath.NormalizeBase(value); }
    public string ConnectionString { get; set; } = string.Empty;
    public long MaxReadBytesPerSecond { get; set; }
    public long MaxWriteBytesPerSecond { get; set; }

    public async Task InitializeAsync(
        string connectionString,
        string baseFolderOrContainer,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await ShutdownAsync(cancellationToken).ConfigureAwait(false);

        SftpClient client = SshConnectionFactory.Create(connectionString);
        try
        {
            await client.ConnectAsync(cancellationToken).ConfigureAwait(false);
            if (!client.IsConnected)
            {
                throw new IOException("The SFTP connection could not be established.");
            }

            _client = client;
            ConnectionString = connectionString;
            _baseFolder = SshPath.NormalizeBase(baseFolderOrContainer);
            if (!await DirectoryExistsAsync("/", cancellationToken).ConfigureAwait(false))
            {
                throw new DirectoryNotFoundException("The configured SFTP base folder does not exist.");
            }
        }
        catch
        {
            client.Dispose();
            _client = null;
            throw;
        }
    }

    public async Task<IntPtr> OpenAsync(
        string realPath,
        OpenFlags flags,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(realPath);
        bool canWrite = FuseFlags.IsWriteAccess(flags) || flags.HasFlag(OpenFlags.Create);
        FileMode mode = SshPath.GetFileMode(flags);
        FileAccess access = canWrite ? FileAccess.ReadWrite : FileAccess.Read;

        if (flags.HasFlag(OpenFlags.CreateParentDirectories))
        {
            string? parent = SshPath.GetParent(path);
            if (parent is not null)
            {
                await CreateDirectoryPathAsync(parent, cancellationToken).ConfigureAwait(false);
            }
        }

        Stream stream = await ExecuteAsync(
            client => client.Open(path, mode, access),
            cancellationToken).ConfigureAwait(false);
        try
        {
            if (flags.HasFlag(OpenFlags.Append))
            {
                stream.Seek(0, SeekOrigin.End);
            }

            var handle = new SshFileHandle(path, stream, !canWrite);
            IntPtr id = new(Interlocked.Increment(ref _nextHandle));
            if (!_handles.TryAdd(id, handle))
            {
                throw new InvalidOperationException("Could not allocate an SFTP file handle.");
            }

            return id;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public async Task CloseAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default)
    {
        if (!_handles.TryGetValue(fileHandle, out SshFileHandle? handle))
        {
            return;
        }

        await handle.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_handles.TryRemove(fileHandle, out SshFileHandle? removedHandle))
            {
                return;
            }

            if (!ReferenceEquals(handle, removedHandle))
            {
                throw new InvalidOperationException("The SFTP file handle changed unexpectedly.");
            }

            handle.Dispose();
        }
        finally
        {
            handle.Lock.Release();
        }
    }

    public Task<int> ReadAsync(
        string path,
        IntPtr fileHandle,
        long offset,
        long size,
        IntPtr buffer,
        CancellationToken cancellationToken = default) =>
        AccessHandleAsync(
            fileHandle,
            handle => SshHandleOperations.ReadAsync(handle, offset, size, buffer, cancellationToken),
            cancellationToken);

    public Task<int> WriteAsync(
        string path,
        IntPtr fileHandle,
        long offset,
        long size,
        IntPtr buffer,
        CancellationToken cancellationToken = default) =>
        AccessHandleAsync(
            fileHandle,
            handle => SshHandleOperations.WriteAsync(handle, offset, size, buffer, cancellationToken),
            cancellationToken);

    public async Task<IEnumerable<FileObject>> ReadDirAsync(
        string realPath,
        bool readOnly = true,
        CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(realPath);
        return await ExecuteAsync(client => client.ListDirectory(path)
            .Where(entry => entry.Name is not "." and not "..")
            .Select(entry => SftpOperations.CreateFileObject(
                this,
                SshPath.ToVirtual(_baseFolder, entry.FullName),
                entry.Name,
                entry.Attributes,
                readOnly))
            .ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await DirectoryExistsAsync("/", cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "SFTP read test failed");
            return false;
        }
    }

    public async Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
    {
        string path = ResolvePath($".storagelib-{Guid.NewGuid():N}.tmp");
        try
        {
            await ExecuteAsync(client =>
            {
                using Stream stream = client.Create(path);
                stream.WriteByte(0);
            }, cancellationToken).ConfigureAwait(false);
            await DeleteAsync(path, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger?.LogWarning(exception, "SFTP write test failed");
            return false;
        }
    }

    public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default) =>
        ExistsAsync(filePath, false, cancellationToken);

    public Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default) =>
        ExistsAsync(directoryPath, true, cancellationToken);

    public async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
    {
        string resolved = ResolvePath(path);
        await ExecuteAsync(client =>
        {
            SftpOperations.DeleteDirectoryRecursive(client, resolved, cancellationToken);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(filePath);
        return await ExecuteAsync(client =>
        {
            var attributes = client.GetAttributes(path);
            string virtualPath = SshPath.ToVirtual(_baseFolder, path);
            return SftpOperations.CreateFileObject(
                this,
                virtualPath,
                SshPath.GetName(virtualPath),
                attributes,
                false);
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        string path = ResolvePath(filePath);
        await ExecuteAsync(client =>
        {
            if (client.Exists(path))
            {
                client.DeleteFile(path);
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task MoveAsync(
        string sourceFilePath,
        string destinationFilePath,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        string source = ResolvePath(sourceFilePath);
        string destination = ResolvePath(destinationFilePath);
        await ExecuteAsync(client =>
        {
            if (client.Exists(destination))
            {
                if (!overwrite)
                {
                    throw new IOException("The SFTP destination already exists.");
                }

                var attributes = client.GetAttributes(destination);
                if (attributes.IsDirectory)
                {
                    SftpOperations.DeleteDirectoryRecursive(client, destination, cancellationToken);
                }
                else
                {
                    client.DeleteFile(destination);
                }
            }

            client.RenameFile(source, destination);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default) =>
        CreateDirectoryPathAsync(ResolvePath(directoryPath), cancellationToken);

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await CloseAllHandlesAsync(cancellationToken).ConfigureAwait(false);
        SftpClient? client = Interlocked.Exchange(ref _client, null);
        if (client is null)
        {
            return;
        }

        await Task.Run(() =>
        {
            if (client.IsConnected)
            {
                client.Disconnect();
            }

            client.Dispose();
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(long.MaxValue);
    }

    public Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(long.MaxValue);
    }

    public async Task CloseAllHandlesAsync(CancellationToken cancellationToken = default)
    {
        foreach (IntPtr handle in _handles.Keys)
        {
            await CloseAsync(string.Empty, handle, cancellationToken).ConfigureAwait(false);
        }
    }

    public Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new NotSupportedException("SSH.NET does not expose an SFTP chown operation.");
    }

    public async Task SetTimesAsync(
        string path,
        DateTime accessTime,
        DateTime modifiedTime,
        CancellationToken cancellationToken = default)
    {
        string resolved = ResolvePath(path);
        await ExecuteAsync(client =>
        {
            var attributes = client.GetAttributes(resolved);
            attributes.LastAccessTime = accessTime;
            attributes.LastWriteTime = modifiedTime;
            client.SetAttributes(resolved, attributes);
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task FlushAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default) =>
        AccessHandleAsync(
            fileHandle,
            handle => SshHandleOperations.FlushAsync(handle, cancellationToken),
            cancellationToken);

    public Task FAllocateAsync(
        string path,
        IntPtr fileHandle,
        int mode,
        long offset,
        long length,
        CancellationToken cancellationToken = default) =>
        AccessHandleAsync(
            fileHandle,
            handle => SshHandleOperations.AllocateAsync(handle, mode, offset, length),
            cancellationToken);

    public async Task TruncateAsync(
        string path,
        IntPtr fileHandle,
        long size,
        CancellationToken cancellationToken = default)
    {
        if (size < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }

        if (fileHandle != IntPtr.Zero)
        {
            await AccessHandleAsync(
                fileHandle,
                handle => SshHandleOperations.TruncateAsync(handle, size),
                cancellationToken).ConfigureAwait(false);
            return;
        }

        string resolvedPath = ResolvePath(path);
        await ExecuteAsync(client =>
        {
            using Stream stream = client.Open(resolvedPath, FileMode.Open, FileAccess.Write);
            stream.SetLength(size);
        }, cancellationToken).ConfigureAwait(false);
    }

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
            _clientLock.Dispose();
        }
    }

    private async Task<TResult> AccessHandleAsync<TResult>(
        IntPtr fileHandle,
        Func<SshFileHandle, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!_handles.TryGetValue(fileHandle, out SshFileHandle? handle))
        {
            throw new InvalidOperationException("The SFTP file handle is invalid.");
        }

        await handle.Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (handle.IsClosed)
            {
                throw new InvalidOperationException("The SFTP file handle is closed.");
            }

            await _clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await operation(handle).ConfigureAwait(false);
            }
            finally
            {
                _clientLock.Release();
            }
        }
        finally
        {
            handle.Lock.Release();
        }
    }

    private async Task<TResult> ExecuteAsync<TResult>(
        Func<SftpClient, TResult> operation,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        await _clientLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            SftpClient client = _client ?? throw new InvalidOperationException("SFTP storage is not initialized.");
            return await Task.Run(() => operation(client), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _clientLock.Release();
        }
    }

    private Task ExecuteAsync(Action<SftpClient> operation, CancellationToken cancellationToken) =>
        ExecuteAsync(client =>
        {
            operation(client);
            return 0;
        }, cancellationToken);

    private async Task<bool> ExistsAsync(string path, bool isDirectory, CancellationToken cancellationToken)
    {
        string resolved = ResolvePath(path);
        return await ExecuteAsync(client =>
        {
            if (!client.Exists(resolved))
            {
                return false;
            }

            return client.GetAttributes(resolved).IsDirectory == isDirectory;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task CreateDirectoryPathAsync(string path, CancellationToken cancellationToken)
    {
        await ExecuteAsync(
            client => SftpOperations.CreateDirectoryPath(client, path),
            cancellationToken).ConfigureAwait(false);
    }

    private string ResolvePath(string path)
    {
        return SshPath.Resolve(_baseFolder, path);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }
}