using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using StorageLib.Abstractions;
using StorageLib.Utilities;
using System.Buffers;

namespace StorageLib.Connectors
{
    /// <summary>
    /// Local file system storage implementation
    /// </summary>
    public class LocalStorage : IStorage, IDisposable
    {
        private const int DefaultFileBufferBytes = 4096;
        private const int UnbufferedFileBufferBytes = 0;

        private string _baseFolder = string.Empty;
        private readonly object _handleLock = new object();
        private readonly Dictionary<IntPtr, FileHandle> _handleMap = new Dictionary<IntPtr, FileHandle>();
        private readonly ILogger<LocalStorage>? _logger;
        private bool _isDisposed;

        public EnumStorageType StorageType => EnumStorageType.Local;

        /// <summary>
        /// Maximum read speed in bytes per second. 0 means no throttling.
        /// Note: Throttling should be handled by ThrottlingDecorator, not by storage connectors directly.
        /// </summary>
        public long MaxReadBytesPerSecond { get; set; } = 0;

        /// <summary>
        /// Maximum write speed in bytes per second. 0 means no throttling.
        /// Note: Throttling should be handled by ThrottlingDecorator, not by storage connectors directly.
        /// </summary>
        public long MaxWriteBytesPerSecond { get; set; } = 0;

        public virtual EnumStorageType Type
        {
            get => EnumStorageType.Local;
            set { } // Empty setter - ignores the value
        }

        public string BaseFolderOrContainer
        {
            get
            {
                ThrowIfDisposed();
                return _baseFolder;
            }
            set
            {
                ThrowIfDisposed();
                _baseFolder = value ?? throw new ArgumentNullException(nameof(value));
            }
        }

        public virtual string ConnectionString { get => "file://"; set { } }

        public LocalStorage(ILogger<LocalStorage>? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Initialize or set up the storage connection asynchronously
        /// </summary>
        public virtual async Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(baseFolderOrContainer))
                    throw new ArgumentException("Base folder cannot be null or empty", nameof(baseFolderOrContainer));

                // For local storage, we need the actual Windows/Linux path, not the normalized storage path
                _baseFolder = baseFolderOrContainer;

                // Ensure directory exists
                if (!Directory.Exists(_baseFolder))
                {
                    throw new DirectoryNotFoundException($"Base directory does not exist: {_baseFolder}");
                }

                _logger?.LogDebug("LocalStorage initialized with base folder: {BaseFolder}", _baseFolder);
            }, cancellationToken);
        }

        /// <summary>
        /// Open a file asynchronously with the specified flags
        /// </summary>
        public virtual async Task<IntPtr> OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("Path cannot be null or empty", nameof(path));

                // Convert virtual path to real file system path
                string realPath = GetRealPath(path);

                FileAccess access = FileAccess.Read;
                FileMode mode = FileMode.Open;
                
                // WINDOWS FIX: Add FileShare.Delete on Windows to allow rename/move operations
                // Windows requires ALL open handles to have FILE_SHARE_DELETE for File.Move() to work
                FileShare share = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) 
                    ? FileShare.Read | FileShare.Write | FileShare.Delete
                    : FileShare.Read | FileShare.Write;

                // Parse FUSE flags
                if (flags.HasFlag(OpenFlags.WriteOnly) || flags.HasFlag(OpenFlags.ReadWrite))
                {
                    // Always use ReadWrite for write operations to support .NET's internal read requirements
                    // Many .NET file operations (like File.AppendAllText) need read access even when opened as WriteOnly
                    access = FileAccess.ReadWrite;
                    // Keep the more permissive sharing for write operations + DELETE on Windows for rename/move
                    share = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                        ? FileShare.Read | FileShare.Write | FileShare.Delete
                        : FileShare.Read | FileShare.Write;
                }

                if (flags.HasFlag(OpenFlags.Create))
                {
                    mode = flags.HasFlag(OpenFlags.Exclusive) ? FileMode.CreateNew : FileMode.OpenOrCreate;
                    
                    // When creating a file, we need write access unless specifically ReadWrite
                    if (access == FileAccess.Read)
                    {
                        access = FileAccess.Write;
                        // Use consistent sharing policy + DELETE on Windows for rename/move
                        share = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                            ? FileShare.Read | FileShare.Write | FileShare.Delete
                            : FileShare.Read | FileShare.Write;
                    }
                }

                if (flags.HasFlag(OpenFlags.Truncate))
                {
                    mode = FileMode.Create;
                }

                if (flags.HasFlag(OpenFlags.Append))
                {
                    mode = FileMode.Append;
                    // For append mode, we need ReadWrite access to support both reading and writing
                    // FileMode.Append with FileAccess.Write creates a write-only stream which breaks read operations
                    access = FileAccess.ReadWrite;
                }

                try
                {
                    // Ensure parent directory exists if creating or if CreateParentDirectories flag is set
                    if (flags.HasFlag(OpenFlags.Create) || flags.HasFlag(OpenFlags.CreateParentDirectories))
                    {
                        string? parentDir = Path.GetDirectoryName(realPath);
                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                        {
                            // Directory.CreateDirectory is recursive (like mkdir -p) - handles full chain
                            Directory.CreateDirectory(parentDir);
                            _logger?.LogDebug("Created parent directory chain for: {Path}", parentDir);
                        }
                    }

                    // Write handles skip .NET's buffer (Windows still caches): with it, a full disk only
                    // shows when the buffer is written at close, too late for the caller to do anything
                    // about it, and the buffered bytes are lost.
                    int bufferSize = access == FileAccess.Read ? DefaultFileBufferBytes : UnbufferedFileBufferBytes;
                    var stream = new FileStream(realPath, mode, access, share, bufferSize);
                    bool isAppendMode = flags.HasFlag(OpenFlags.Append);
                    var handle = new FileHandle(realPath, stream, access == FileAccess.Read, this, isAppendMode);

                    lock (_handleLock)
                    {
                        IntPtr handlePtr = handle.CreateContext();
                        _handleMap[handlePtr] = handle;
                        
                        _logger?.LogTrace("Opened file {Path} with handle {Handle}", realPath, handlePtr);
                        return handlePtr;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error opening file {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Close a file asynchronously
        /// </summary>
        public virtual async Task CloseAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                
                    if (_handleMap.TryGetValue(fileHandle, out var handle))
                    {
                        try
                        {
                    // Ensure all data is flushed before closing (important for write handles)
                    if (!handle.IsReadOnly)
                    {
                        await handle.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }
                    
                            handle.Stream?.Dispose();
                    
                    lock (_handleLock)
                    {
                            _handleMap.Remove(fileHandle);
                    }
                            
                            _logger?.LogTrace("Closed file handle {Handle} for {Path}", fileHandle, handle.Path);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogError(ex, "Error closing file handle {Handle}", fileHandle);
                            throw;
                        }
                    }
        }

        /// <summary>
        /// Read data from a file asynchronously
        /// </summary>
        public virtual async Task<int> ReadAsync(string path, IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                if (buffer == IntPtr.Zero)
                    throw new ArgumentException("Buffer cannot be null", nameof(buffer));
                if (size < 0)
                    throw new ArgumentException("Size cannot be negative", nameof(size));

                int bytesRead = 0;

            // Get handle with minimal locking
                    if (!_handleMap.TryGetValue(fileHandle, out var handle))
                        throw new InvalidOperationException($"Invalid file handle: {fileHandle}");

                    try
                    {
                // Seek to the specified offset (inherently synchronous, that's OK)
                        handle.Stream.Seek(offset, SeekOrigin.Begin);

                // Straight into the caller's buffer: a 4 MB read is one copy less, and the buffer's pages are
                // touched by the read itself instead of by a second copy afterwards.
                using var target = new NativeBufferMemoryManager(buffer, (int)size);
                bytesRead = await handle.Stream.ReadAsync(target.Memory, cancellationToken).ConfigureAwait(false);

#if DEBUG
                        _logger?.LogTrace("Read {BytesRead} bytes from {Path} at offset {Offset}", 
                            bytesRead, handle.Path, offset);
#endif
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error reading from file {Path}", handle.Path);
                        throw;
                    }

            return bytesRead;
        }

        /// <summary>
        /// Write data to a file asynchronously
        /// </summary>
        public virtual async Task<int> WriteAsync(string path, IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
            {
                ThrowIfDisposed();
                if (buffer == IntPtr.Zero)
                    throw new ArgumentException("Buffer cannot be null", nameof(buffer));
                if (size < 0)
                    throw new ArgumentException("Size cannot be negative", nameof(size));

            // Get handle with minimal locking
                    if (!_handleMap.TryGetValue(fileHandle, out var handle))
                        throw new InvalidOperationException($"Invalid file handle: {fileHandle}");

                    if (handle.IsReadOnly)
                        throw new UnauthorizedAccessException("Cannot write to read-only file");

                    try
                    {
                // Seek to the specified offset (inherently synchronous, that's OK)
                        handle.Stream.Seek(offset, SeekOrigin.Begin);
                
                using var source = new NativeBufferMemoryManager(buffer, (int)size);
                await handle.Stream.WriteAsync(source.Memory, cancellationToken).ConfigureAwait(false);

#if DEBUG
                        _logger?.LogTrace("Wrote {Size} bytes to {Path} at offset {Offset}", 
                            size, handle.Path, offset);
#endif
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error writing to file {Path}", handle.Path);
                        throw;
                    }

            return (int)size;
                }


        /// <summary>
        /// Flush file buffers to disk asynchronously
        /// </summary>
        public virtual async Task FlushAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (_handleMap.TryGetValue(fileHandle, out var handle))
            {
                try
                {
                    await handle.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
#if DEBUG
                    _logger?.LogTrace("Flushed buffers for {Path}", handle.Path);
#endif
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error flushing file {Path}", handle.Path);
                    throw;
                }
            }
        }

        /// <summary>
        /// Allocate space for a file handle asynchronously (pre-allocation for performance)
        /// </summary>
        public virtual async Task FAllocateAsync(string path, IntPtr fileHandle, int mode, long offset, long length, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            if (_handleMap.TryGetValue(fileHandle, out var handle))
            {
                try
                {
                    // Calculate new allocation size: max(offset + length, current length)
                    long currentLength = handle.Stream.Length;
                    long newAllocationSize = Math.Max(offset + length, currentLength);
                    
                    // Check FALLOC_FL_KEEP_SIZE flag (value 1)
                    bool keepSize = (mode & 1) != 0;
                    
                    // Set the length if not keeping size and allocation is larger
                    if (!keepSize && newAllocationSize > currentLength)
                    {
                        // Extend file and zero-fill the new space
                        long oldPosition = handle.Stream.Position;
                        
                        // Set new length
                        handle.Stream.SetLength(newAllocationSize);
                        
                        // Zero-fill the newly allocated space
                        handle.Stream.Seek(currentLength, SeekOrigin.Begin);
                        long bytesToZero = newAllocationSize - currentLength;
                        
                        byte[] zeroBuffer = new byte[Math.Min(bytesToZero, 64 * 1024)]; // 64KB buffer
                        while (bytesToZero > 0)
                        {
                            int bytesToWrite = (int)Math.Min(bytesToZero, zeroBuffer.Length);
                            await handle.Stream.WriteAsync(zeroBuffer, 0, bytesToWrite, cancellationToken).ConfigureAwait(false);
                            bytesToZero -= bytesToWrite;
                        }
                        
                        // Restore original position
                        handle.Stream.Seek(oldPosition, SeekOrigin.Begin);
                    }
                    else if (newAllocationSize > handle.Stream.Length)
                    {
                        // Even with KEEP_SIZE, we need to ensure space is available
                        // This is implementation-specific - for FileStream, setting length ensures space
                        long oldPosition = handle.Stream.Position;
                        handle.Stream.SetLength(newAllocationSize);
                        handle.Stream.Seek(oldPosition, SeekOrigin.Begin);
                    }
                    
#if DEBUG
                    _logger?.LogTrace("Allocated space for {Path}: mode={Mode}, offset={Offset}, length={Length}", 
                        handle.Path, mode, offset, length);
#endif
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Failed to allocate space for {Path}", handle.Path);
                    throw;
                }
            }
            else
            {
                throw new ArgumentException($"Invalid file handle: {fileHandle}", nameof(fileHandle));
            }
        }
        
        /// <summary>
        /// Read entire file asynchronously
        /// </summary>
        public virtual async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                string realPath = GetRealPath(path);
                
                try
                {
                    byte[] data = File.ReadAllBytes(realPath);
                    _logger?.LogTrace("Read entire file {Path}, {Size} bytes", realPath, data.Length);
                    return data;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error reading entire file {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }



        /// <summary>
        /// Read directory contents (files and subdirectories) asynchronously
        /// </summary>
        public virtual async Task<IEnumerable<FileObject>> ReadDirAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(realPath))
                    throw new ArgumentException("Path cannot be null or empty", nameof(realPath));

                string fsPath = GetRealPath(realPath);
                var fileObjects = new List<FileObject>();

                try
                {
                    // Enumerate files
                    foreach (var file in Directory.GetFiles(fsPath))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            var fileObject = new FileObject(GetVirtualPath(file))
                            {
                                Filename = fileInfo.Name,
                                RealPath = GetVirtualPath(file),
                                VirtualPath = GetVirtualPath(file),
                                IsDirectory = false,
                                Size = fileInfo.Length,
                                CreationTime = fileInfo.CreationTime,
                                LastModified = fileInfo.LastWriteTime,
                                LastAccessTime = fileInfo.LastAccessTime,
                                SC = this
                            };
                            fileObjects.Add(fileObject);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Error processing file {File}", file);
                            // Continue with next file
                        }
                    }

                    // Enumerate directories
                    foreach (var directory in Directory.GetDirectories(fsPath))
                    {
                        try
                        {
                            var dirInfo = new DirectoryInfo(directory);
                            var dirObject = new FileObject(GetVirtualPath(directory))
                            {
                                Filename = dirInfo.Name,
                                RealPath = GetVirtualPath(directory),
                                VirtualPath = GetVirtualPath(directory),
                                IsDirectory = true,
                                Size = 0,
                                CreationTime = dirInfo.CreationTime,
                                LastModified = dirInfo.LastWriteTime,
                                LastAccessTime = dirInfo.LastAccessTime,
                                SC = this
                            };
                            fileObjects.Add(dirObject);
                        }
                        catch (Exception ex)
                        {
                            _logger?.LogWarning(ex, "Error processing directory {Directory}", directory);
                            // Continue with next directory
                        }
                    }

                    _logger?.LogDebug("Enumerated {Count} entries in directory: {Path}", fileObjects.Count, fsPath);
                    return fileObjects.AsEnumerable();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error enumerating files and directories in {Path}", fsPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Test if storage is readable asynchronously
        /// </summary>
        public virtual async Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(_baseFolder))
                    return false;

                try
                {
                    // Test if we can read the directory
                    return Directory.Exists(_baseFolder);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error testing read access for {BaseFolder}", _baseFolder);
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Test if storage is writable asynchronously
        /// </summary>
        public virtual async Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(_baseFolder))
                    return false;

                try
                {
                    var testFile = Path.Combine(_baseFolder, $"test_{Guid.NewGuid()}.tmp");
                    using (var stream = File.Create(testFile))
                    {
                        stream.WriteByte(0);
                    }

                    try
                    {
                        File.Delete(testFile);
                        return true;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "Failed to delete test file {TestFile}", testFile);
                        return true; // Still return true as write was successful
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error testing write access for {BaseFolder}", _baseFolder);
                    return false;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Check if a file exists asynchronously
        /// </summary>
        public virtual Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return RunInline(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("File path cannot be null or empty", nameof(filePath));

                string realPath = GetRealPath(filePath);
                
                try
                {
                    bool exists = File.Exists(realPath);
                    _logger?.LogTrace("File exists check for {Path}: {Exists}", realPath, exists);
                    return exists;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error checking file existence for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Check if a directory exists asynchronously
        /// </summary>
        public virtual Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return RunInline(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                string realPath = GetRealPath(directoryPath);
                
                try
                {
                    bool exists = Directory.Exists(realPath);
                    _logger?.LogTrace("Directory exists check for {Path}: {Exists}", realPath, exists);
                    return exists;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error checking directory existence for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Delete a directory asynchronously
        /// </summary>
        public virtual async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(path))
                    throw new ArgumentException("Path cannot be null or empty", nameof(path));

                string realPath = GetRealPath(path);
                
                try
                {
                    if (Directory.Exists(realPath))
                    {
                        Directory.Delete(realPath, true);
                        _logger?.LogDebug("Deleted directory: {Path}", realPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error deleting directory {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }



        /// <summary>
        /// Get file information asynchronously
        /// </summary>
        public virtual Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return RunInline(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("Path cannot be null or empty", nameof(filePath));

                string realPath = GetRealPath(filePath);
                
                try
                {
                    // One query answers both "does it exist" and all its attributes; FileInfo keeps what it read.
                    var fileInfo = new FileInfo(realPath);
                    bool isFile = fileInfo.Exists;
                    var dirInfo = isFile ? null : new DirectoryInfo(realPath);

                    if (!isFile && !dirInfo!.Exists)
                    {
                        throw new FileNotFoundException("The specified path does not exist.", realPath);
                    }

                    var fileObject = new FileObject(filePath)
                    {
                        IsDirectory = !isFile,
                        RealPath = filePath,
                        VirtualPath = filePath,
                        SC = this
                    };

                    if (isFile)
                    {
                        fileObject.Size = fileInfo.Length;
                        fileObject.CreationTime = fileInfo.CreationTime;
                        fileObject.LastModified = fileInfo.LastWriteTime;
                        fileObject.LastAccessTime = fileInfo.LastAccessTime;
                        fileObject.Filename = fileInfo.Name;
                    }
                    else
                    {
                        fileObject.CreationTime = dirInfo!.CreationTime;
                        fileObject.LastModified = dirInfo.LastWriteTime;
                        fileObject.LastAccessTime = dirInfo.LastAccessTime;
                        fileObject.Filename = dirInfo.Name;
                        fileObject.Size = 0;
                    }

                    _logger?.LogTrace("Retrieved file info for: {Path}", realPath);
                    return fileObject;
                }
                catch (Exception ex) when (!(ex is FileNotFoundException))
                {
                    _logger?.LogError(ex, "Error getting file info for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Delete a file asynchronously
        /// </summary>
        public virtual async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(filePath))
                    throw new ArgumentException("Path cannot be null or empty", nameof(filePath));

                string realPath = GetRealPath(filePath);
                
                try
                {
                    if (File.Exists(realPath))
                    {
                        File.Delete(realPath);
                        _logger?.LogDebug("Deleted file: {Path}", realPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error deleting file {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Move/rename a file or directory asynchronously (unified method with auto-detection)
        /// </summary>
        public virtual async Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(sourcePath))
                    throw new ArgumentException("Source path cannot be null or empty", nameof(sourcePath));
                if (string.IsNullOrEmpty(destinationPath))
                    throw new ArgumentException("Destination path cannot be null or empty", nameof(destinationPath));

                string realSourcePath = GetRealPath(sourcePath);
                string realDestPath = GetRealPath(destinationPath);
                
                try
                {
                    // Auto-detect: Check what we're moving (directory takes precedence over file)
                    bool isSourceDirectory = Directory.Exists(realSourcePath);
                    bool isSourceFile = !isSourceDirectory && File.Exists(realSourcePath);
                    
                    if (!isSourceDirectory && !isSourceFile)
                    {
                        throw new FileNotFoundException($"Source path not found: {sourcePath}");
                    }
                    
                    if (isSourceDirectory)
                    {
                        // Move directory
                        MoveDirectoryInternal(realSourcePath, realDestPath, overwrite);
                        _logger?.LogDebug("Moved directory from {Source} to {Destination}", realSourcePath, realDestPath);
                    }
                    else
                    {
                        // Move file (existing logic)
                        MoveFileInternal(realSourcePath, realDestPath, overwrite);
                        _logger?.LogDebug("Moved file from {Source} to {Destination}", realSourcePath, realDestPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error moving {Type} from {Source} to {Destination}", 
                        Directory.Exists(realSourcePath) ? "directory" : "file", realSourcePath, realDestPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Internal method to move a file
        /// </summary>
        private void MoveFileInternal(string realSourcePath, string realDestPath, bool overwrite)
        {
            if (overwrite && File.Exists(realDestPath))
            {
                File.Delete(realDestPath);
            }
            
            // Ensure destination directory exists
            string? destDir = Path.GetDirectoryName(realDestPath);
            if (!string.IsNullOrEmpty(destDir) && !Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
            
            File.Move(realSourcePath, realDestPath);
        }

        /// <summary>
        /// Internal method to move a directory
        /// </summary>
        private void MoveDirectoryInternal(string realSourcePath, string realDestPath, bool overwrite)
        {
            // Check if destination exists
            if (Directory.Exists(realDestPath))
            {
                if (!overwrite)
                {
                    throw new IOException($"Destination directory already exists: {realDestPath}");
                }
                // If overwrite is true, delete the destination directory
                Directory.Delete(realDestPath, true);
            }
            else if (File.Exists(realDestPath))
            {
                if (!overwrite)
                {
                    throw new IOException($"Destination file already exists: {realDestPath}");
                }
                File.Delete(realDestPath);
            }
            
            // Ensure parent directory of destination exists
            string? destParentDir = Path.GetDirectoryName(realDestPath);
            if (!string.IsNullOrEmpty(destParentDir) && !Directory.Exists(destParentDir))
            {
                Directory.CreateDirectory(destParentDir);
            }
            
            // Use Directory.Move for directory operations
            Directory.Move(realSourcePath, realDestPath);
        }

        /// <summary>
        /// Create a directory asynchronously
        /// </summary>
        public virtual async Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                if (string.IsNullOrEmpty(directoryPath))
                    throw new ArgumentException("Directory path cannot be null or empty", nameof(directoryPath));

                string realPath = GetRealPath(directoryPath);
                
                try
                {
                    if (!Directory.Exists(realPath))
                    {
                        Directory.CreateDirectory(realPath);
                        _logger?.LogDebug("Created directory: {Path}", realPath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error creating directory {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Shutdown the storage connection asynchronously
        /// </summary>
        public virtual async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            await CloseAllHandlesAsync(cancellationToken);
            _logger?.LogDebug("LocalStorage shutdown completed");
        }

        /// <summary>
        /// Get the total storage capacity asynchronously
        /// </summary>
        public virtual async Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                try
                {
                    if (string.IsNullOrEmpty(_baseFolder) || !Directory.Exists(_baseFolder))
                        return 0;

                    var drive = new DriveInfo(Path.GetPathRoot(_baseFolder) ?? _baseFolder);
                    return drive.IsReady ? drive.TotalSize : 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error getting total capacity for {BaseFolder}", _baseFolder);
                    return 0;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Get the available free space asynchronously
        /// </summary>
        public virtual async Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                ThrowIfDisposed();
                try
                {
                    if (string.IsNullOrEmpty(_baseFolder) || !Directory.Exists(_baseFolder))
                        return 0;

                    var drive = new DriveInfo(Path.GetPathRoot(_baseFolder) ?? _baseFolder);
                    return drive.IsReady ? drive.AvailableFreeSpace : 0;
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error getting available free space for {BaseFolder}", _baseFolder);
                    return 0;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Close all open file handles asynchronously
        /// </summary>
        public virtual async Task CloseAllHandlesAsync(CancellationToken cancellationToken = default)
        {
            List<(IntPtr handle, string path)> handlesCopy;
            lock (_handleLock)
            {
                handlesCopy = _handleMap.Select(kvp => (kvp.Key, kvp.Value.Path)).ToList();
            }
            
            // Close handles outside the lock to avoid deadlock
            foreach (var (handle, path) in handlesCopy)
            {
                await CloseAsync(path, handle, cancellationToken);
            }
            
            _logger?.LogDebug("Closed all file handles");
        }

        /// <summary>
        /// Change file ownership asynchronously (no-op for local storage on Windows, limited on Linux)
        /// </summary>
        public virtual async Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                ThrowIfDisposed();
                string realPath = GetRealPath(path);
                _logger?.LogDebug("Chown operation (limited/no-op) for {Path} (UID={Uid}, GID={Gid})", realPath, uid, gid);
                
                // On Windows, this is essentially a no-op
                // On Linux, we could use native calls but that's complex and not commonly needed
            }, cancellationToken);
        }

        /// <summary>
        /// Set file times asynchronously
        /// </summary>
        public virtual async Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (FindOpenWriteStream(GetRealPath(path)) is { } openStream)
            {
                // By path, each time would open the file once more, and data still buffered in the open stream
                // would be written afterwards and move the modification time forward again.
                await openStream.FlushAsync(cancellationToken).ConfigureAwait(false);
                File.SetLastAccessTime(openStream.SafeFileHandle, accessTime);
                File.SetLastWriteTime(openStream.SafeFileHandle, modifiedTime);
                return;
            }

            await Task.Run(() =>
            {
                ThrowIfDisposed();
                string realPath = GetRealPath(path);
                
                try
                {
                    File.SetLastAccessTime(realPath, accessTime);
                    File.SetLastWriteTime(realPath, modifiedTime);
                    
                    _logger?.LogDebug("Updated times for {Path}", realPath);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error setting timestamps for {Path}", realPath);
                    throw;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// The stream of a handle this storage has open for writing on <paramref name="realPath"/>, or null.
        /// </summary>
        private FileStream? FindOpenWriteStream(string realPath)
        {
            lock (_handleLock)
            {
                foreach (var handle in _handleMap.Values)
                {
                    if (!handle.IsReadOnly && handle.Stream is FileStream stream &&
                        string.Equals(handle.Path, realPath, System.OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    {
                        return stream;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Runs a quick metadata query on the calling thread. Handing it to the thread pool cost more than
        /// the query itself, and the callers wait for the answer anyway.
        /// </summary>
        private static Task<T> RunInline<T>(Func<T> query, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<T>(cancellationToken);
            }

            try
            {
                return Task.FromResult(query());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }

        /// <summary>
        /// Convert virtual storage path to real file system path
        /// </summary>
        private string GetRealPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath) || virtualPath == ".")
                return _baseFolder;

            // Normalize the virtual path using our PathHelper
            string normalizedVirtualPath = PathHelper.NormalizePath(virtualPath);
            
            // Remove leading slash for combination
            string relativePath = normalizedVirtualPath.TrimStart('/');
            
            // Handle "." after normalization as well
            if (relativePath == ".")
                return _baseFolder;
            
            return Path.Combine(_baseFolder, relativePath);
        }

        /// <summary>
        /// Convert real file system path to virtual storage path
        /// </summary>
        private string GetVirtualPath(string realPath)
        {
            if (string.IsNullOrEmpty(realPath) || !realPath.StartsWith(_baseFolder))
                return "/";

            string relativePath = realPath.Substring(_baseFolder.Length);
            return PathHelper.NormalizePath("/" + relativePath.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }

        private void ThrowIfDisposed()
        {
            if (_isDisposed)
                throw new ObjectDisposedException(nameof(LocalStorage));
        }

        public virtual async Task TruncateAsync(string path, IntPtr fileHandle, long size, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            
            // FileHandle is always valid in our system - FUSE gives us back our own handles
            if (!_handleMap.TryGetValue(fileHandle, out var handle))
                throw new InvalidOperationException($"Invalid file handle: {fileHandle}");

            try
            {
                long currentLength = handle.Stream.Length;
                handle.Stream.SetLength(size);
                
                // If expanding file, fill new space with null bytes
                if (size > currentLength)
                {
                    long oldPosition = handle.Stream.Position;
                    handle.Stream.Seek(currentLength, SeekOrigin.Begin);
                    long bytesToZero = size - currentLength;
                    byte[] zeroBuffer = new byte[Math.Min(bytesToZero, 64 * 1024)];
                    
                    while (bytesToZero > 0)
                    {
                        int bytesToWrite = (int)Math.Min(bytesToZero, zeroBuffer.Length);
                        await handle.Stream.WriteAsync(zeroBuffer, 0, bytesToWrite, cancellationToken).ConfigureAwait(false);
                        bytesToZero -= bytesToWrite;
                    }
                    
                    handle.Stream.Seek(oldPosition, SeekOrigin.Begin);
                }
                
#if DEBUG
                _logger?.LogTrace("Truncated file {Path} to size {Size}", handle.Path, size);
#endif
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Failed to truncate file {Path}", handle.Path);
                throw;
            }
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && !_isDisposed)
            {
                lock (_handleLock)
                {
                    foreach (var handle in _handleMap.Values)
                    {
                        handle.Stream?.Dispose();
                    }
                    _handleMap.Clear();
                }
                
                _isDisposed = true;
                _logger?.LogDebug("LocalStorage disposed");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
} 