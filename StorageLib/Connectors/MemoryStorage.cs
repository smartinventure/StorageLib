using StorageLib.Abstractions;
using StorageLib.Utilities;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using System.Buffers;

namespace StorageLib.Connectors
{
    /// <summary>
    /// In-memory storage implementation for testing and virtualization scenarios.
    /// Stores all files and directories in memory with full file system semantics.
    /// </summary>
    public class MemoryStorage : IStorage, IDisposable
    {
        private string _baseFolder = string.Empty;
        private Dictionary<string, FileObject> _memoryFiles;
        private Dictionary<string, HashSet<string>> _directoryContents;
        private readonly object _handleLock = new object();
        private Dictionary<IntPtr, FileHandle> _handleMap = new Dictionary<IntPtr, FileHandle>();
        private readonly ILogger<MemoryStorage>? _logger;

        public EnumStorageType StorageType => EnumStorageType.Memory;

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

        /// <summary>
        /// Storage capacity limit in bytes. 0 or negative means unlimited (long.MaxValue).
        /// Default is unlimited for backwards compatibility.
        /// </summary>
        public long LimitStorage { get; set; } = 0;

        public virtual EnumStorageType Type
        {
            get => EnumStorageType.Memory;
            set { } // Empty setter - ignores the value
        }

        public string BaseFolderOrContainer
        {
            get => _baseFolder;
            set => _baseFolder = value;
        }

        public virtual string ConnectionString { get => "memory://"; set { } }

        public MemoryStorage(ILogger<MemoryStorage>? logger = null)
        {
            _logger = logger;
            _memoryFiles = new Dictionary<string, FileObject>();
            _directoryContents = new Dictionary<string, HashSet<string>>();
        }

        /// <summary>
        /// Initialize or set up the storage connection asynchronously
        /// </summary>
        public virtual async Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                _baseFolder = baseFolderOrContainer;
                _memoryFiles = new Dictionary<string, FileObject>();
                _directoryContents = new Dictionary<string, HashSet<string>>();
                
                // Automatically create root directory
                string rootPath = "/";
                _directoryContents[rootPath] = new HashSet<string>();
                _memoryFiles[rootPath] = new FileObject(rootPath)
                {
                    Filename = "",
                    RealPath = rootPath,
                    VirtualPath = rootPath,
                    IsDirectory = true,
                    CreationTime = DateTime.Now,
                    LastModified = DateTime.Now,
                    LastAccessTime = DateTime.Now,
                    SC = this
                };
                
                _logger?.LogDebug("MemoryStorage initialized with base folder: {BaseFolder}", _baseFolder);
            }, cancellationToken);
        }

        /// <summary>
        /// Open a file asynchronously with the specified flags
        /// </summary>
        public virtual async Task<IntPtr> OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                lock (_handleLock)
                {
                    // Normalize path to Linux-style for storage operations
                    path = PathHelper.NormalizePath(path);

                    bool isCreating = flags.HasFlag(OpenFlags.Create);
                    bool isWriting = flags.HasFlag(OpenFlags.WriteOnly) || flags.HasFlag(OpenFlags.ReadWrite);

                    // When creating a file, check if parent directory exists first
                    if (isCreating || flags.HasFlag(OpenFlags.CreateParentDirectories))
                    {
                        string? parentDir = PathHelper.GetParentDirectory(path);
                        if (parentDir == null) parentDir = "/";

                        // If CreateParentDirectories flag is set, create missing parent directories
                        if (flags.HasFlag(OpenFlags.CreateParentDirectories))
                        {
                            CreateParentDirectoriesInMemory(path);
                            _logger?.LogDebug("Created parent directory chain in memory for: {Path}", path);
                        }
                        // Otherwise, check if the parent directory exists
                        else if (!_directoryContents.ContainsKey(parentDir) && !parentDir.Equals("/"))
                        {
                            throw new DirectoryNotFoundException($"Parent directory does not exist: {parentDir}");
                        }
                    }

                    // Check for O_EXCL: If file exists and O_EXCL is set, throw error
                    if (_memoryFiles.ContainsKey(path) && flags.HasFlag(OpenFlags.Exclusive))
                    {
                        throw new IOException($"File exists: {path}");
                    }

                    // If file doesn't exist and we are not creating, throw error
                    if (!_memoryFiles.ContainsKey(path) && !isCreating)
                    {
                        throw new FileNotFoundException($"File not found: {path}");
                    }

                    // Create the file if it doesn't exist and O_CREAT is specified
                    if (!_memoryFiles.ContainsKey(path) && isCreating)
                    {
                        _memoryFiles[path] = new FileObject(path)
                        {
                            Bytes = new byte[0],
                            Filename = path, // Use full path instead of just filename
                            RealPath = path,
                            VirtualPath = path,
                            IsDirectory = false,
                            CreationTime = DateTime.Now,
                            LastModified = DateTime.Now,
                            LastAccessTime = DateTime.Now,
                            SC = this
                        };

                        // Add to parent directory entries
                        AddToParentDirectory(path);
                        _logger?.LogDebug("Created new file in memory: {Path}", path);
                    }

                    // Get the file object
                    var fileObject = _memoryFiles[path];

                    // Handle O_TRUNC: If flag is set and it's a file, clear content
                    if (flags.HasFlag(OpenFlags.Truncate) && !fileObject.IsDirectory)
                    {
                        fileObject.Bytes = Array.Empty<byte>();
                        fileObject.Size = 0;
                        _logger?.LogDebug("Truncated file: {Path}", path);
                    }

                    // Check if it's a directory and if O_DIRECTORY flag is set appropriately
                    if (fileObject.IsDirectory && !flags.HasFlag(OpenFlags.Directory))
                    {
                        throw new IOException($"Not a file: {path}");
                    }

                    if (!fileObject.IsDirectory && flags.HasFlag(OpenFlags.Directory))
                    {
                        throw new IOException($"Not a directory: {path}");
                    }

                    // Create an appropriate stream for the file object
                    Stream? fileStream = null;
                    if (!fileObject.IsDirectory)
                    {
                        fileStream = new MemoryStream();
                        if (fileObject.Bytes != null)
                        {
                            // Write existing bytes to the stream
                            fileStream.Write(fileObject.Bytes, 0, fileObject.Bytes.Length);
                            // If O_TRUNC was set, truncate the stream now
                            if (flags.HasFlag(OpenFlags.Truncate))
                            {
                                fileStream.SetLength(0);
                            }
                            
                            // Set position based on open flags
                            if (flags.HasFlag(OpenFlags.Append))
                            {
                                fileStream.Position = fileStream.Length; // Position at end for append
                            }
                            else
                            {
                                fileStream.Position = 0; // Reset position for normal read/write
                            }
                        }
                    }

                    // Determine if this handle should be read-only
                    bool isReadAccess = flags.HasFlag(OpenFlags.ReadOnly) || flags.HasFlag(OpenFlags.ReadWrite);
                    bool isWriteAccess = flags.HasFlag(OpenFlags.WriteOnly) || flags.HasFlag(OpenFlags.ReadWrite);
                    bool isReadOnly = isReadAccess && !isWriteAccess;
                    
                    // For any write operations, allow both reading and writing to support .NET's internal read requirements
                    // Many .NET file operations (like File.AppendAllText) need read access even when opened as WriteOnly
                    if (isWriteAccess)
                    {
                        isReadOnly = false; // Write operations should allow both reading and writing
                    }

                    // Create a new handle with the initialized stream
                    bool isAppendMode = flags.HasFlag(OpenFlags.Append);
                    var handle = new FileHandle(path, fileStream!, isReadOnly, this, isAppendMode);
                    handle.RealPath = path;
                    handle.VirtualPath = fileObject.VirtualPath;

                    IntPtr context = handle.CreateContext();
                    _handleMap[context] = handle;

                    _logger?.LogDebug("Opened file handle for: {Path}, Flags: {Flags}", path, flags);
                    return context;
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Close a file asynchronously
        /// </summary>
        public virtual async Task CloseAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default)
                {
                    if (!_handleMap.TryGetValue(fileHandle, out var handle))
                        return;

                    try
                    {
                // Ensure all data is flushed before closing (important for write handles)
                if (!handle.IsReadOnly)
                {
                    await handle.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                        // If this was a writable handle, update the file's bytes
                        if (!handle.IsReadOnly && handle.Stream is MemoryStream memStream)
                        {
                            if (_memoryFiles.TryGetValue(handle.Path, out var fileObject))
                            {
                                fileObject.Bytes = memStream.ToArray();
                                fileObject.Size = fileObject.Bytes.Length;
                                fileObject.LastModified = DateTime.Now;
                                fileObject.LastAccessTime = DateTime.Now;
                                
                                _logger?.LogDebug("Updated file content for: {Path}, Size: {Size}", handle.Path, fileObject.Size);
                            }
                        }

                        handle.Stream?.Dispose();
                
                lock (_handleLock)
                {
                        _handleMap.Remove(fileHandle);
                }
                        
                        _logger?.LogDebug("Closed file handle for: {Path}", handle.Path);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error closing file handle for: {Path}", handle.Path);
                        throw;
                    }
        }

        /// <summary>
        /// Read data from a file asynchronously
        /// </summary>
        public virtual async Task<int> ReadAsync(string path, IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            if (buffer == IntPtr.Zero)
                throw new ArgumentException("Buffer cannot be null", nameof(buffer));
            if (size < 0)
                throw new ArgumentException("Size cannot be negative", nameof(size));

            int bytesRead = 0;

            // Get handle with minimal locking
                    if (!_handleMap.TryGetValue(fileHandle, out var handle))
                        throw new InvalidOperationException("Invalid file handle");

                    try
                    {
                // Seek to the specified offset (inherently synchronous, that's OK)
                        handle.Stream.Seek(offset, SeekOrigin.Begin);

                // MARSHAL OPTIMIZATION: Use ArrayPool to reduce allocations
                byte[]? rentedArray = null;
                try
                {
                    // Rent from pool instead of allocating new array every time
                    rentedArray = ArrayPool<byte>.Shared.Rent((int)size);
                    
                    // TRUE ASYNC: Use Stream.ReadAsync instead of Stream.Read
                    bytesRead = await handle.Stream.ReadAsync(rentedArray, 0, (int)size, cancellationToken)
                        .ConfigureAwait(false);
                    
                    if (bytesRead > 0)
                    {
                        // PERFORMANCE: Direct marshal copy from pooled array
                        Marshal.Copy(rentedArray, 0, buffer, bytesRead);
                    }
                        
#if DEBUG
                    _logger?.LogTrace("Read {BytesRead} bytes from {Path} at offset {Offset}", 
                        bytesRead, handle.Path, offset);
#endif
                }
                finally
                {
                    // Always return rented array to pool
                    if (rentedArray != null)
                        ArrayPool<byte>.Shared.Return(rentedArray);
                }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error reading from file: {Path}", handle.Path);
                        throw;
                    }

            return bytesRead;
        }

        /// <summary>
        /// Write data to a file asynchronously
        /// </summary>
        public virtual async Task<int> WriteAsync(string path, IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            if (buffer == IntPtr.Zero)
                throw new ArgumentException("Buffer cannot be null", nameof(buffer));
            if (size < 0)
                throw new ArgumentException("Size cannot be negative", nameof(size));

            // Get handle with minimal locking
                    if (!_handleMap.TryGetValue(fileHandle, out var handle))
                        throw new InvalidOperationException("Invalid file handle");

            if (handle.IsReadOnly)
                throw new UnauthorizedAccessException("Cannot write to read-only file");

                    try
                    {
                // Seek to the specified offset (unless in append mode)
                        if (!handle.IsAppendMode)
                        {
                            handle.Stream.Seek(offset, SeekOrigin.Begin);
                        }
                        // In append mode, we stay at the current position (which should be at end)

                // MARSHAL OPTIMIZATION: Use ArrayPool to reduce allocations
                byte[]? rentedArray = null;
                try
                {
                    // Rent from pool instead of allocating new array every time
                    rentedArray = ArrayPool<byte>.Shared.Rent((int)size);
                    
                    // PERFORMANCE: Direct marshal copy to pooled array
                    Marshal.Copy(buffer, rentedArray, 0, (int)size);

                    // TRUE ASYNC: Use Stream.WriteAsync instead of Stream.Write
                    await handle.Stream.WriteAsync(rentedArray, 0, (int)size, cancellationToken)
                        .ConfigureAwait(false);

                        // Update the file object with the new bytes
                        if (handle.Stream is MemoryStream memStream && _memoryFiles.TryGetValue(handle.Path, out var fileObject))
                        {
                            fileObject.Bytes = memStream.ToArray();
                            fileObject.Size = fileObject.Bytes.Length;
                            fileObject.LastModified = DateTime.Now;
                            fileObject.LastAccessTime = DateTime.Now;
                        }
                        
#if DEBUG
                        _logger?.LogTrace("Wrote {BytesWritten} bytes to {Path} at offset {Offset}", size, handle.Path, offset);
#endif
                }
                finally
                {
                    // Always return rented array to pool
                    if (rentedArray != null)
                        ArrayPool<byte>.Shared.Return(rentedArray);
                }
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogError(ex, "Error writing to file: {Path}", handle.Path);
                        throw;
                    }

            return (int)size;
                }

        
        /// <summary>
        /// Flush file buffers to disk asynchronously
        /// </summary>
        public virtual async Task FlushAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default)
        {
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

        public virtual async Task FAllocateAsync(string path, IntPtr fileHandle, int mode, long offset, long length, CancellationToken cancellationToken = default)
        {
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
                        // Extend memory stream and zero-fill the new space
                        long oldPosition = handle.Stream.Position;
                        
                        // Set new length (MemoryStream automatically zero-fills)
                        handle.Stream.SetLength(newAllocationSize);
                        
                        // Restore original position
                        handle.Stream.Seek(oldPosition, SeekOrigin.Begin);
                    }
                    else if (newAllocationSize > handle.Stream.Length)
                    {
                        // Even with KEEP_SIZE, we need to ensure space is available
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
        /// Read file data into a byte array asynchronously
        /// </summary>
        public virtual async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                path = PathHelper.NormalizePath(path);
                
                if (!_memoryFiles.ContainsKey(path))
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }
                
                _logger?.LogDebug("Read file content for: {Path}", path);
                return _memoryFiles[path].Bytes ?? Array.Empty<byte>();
            }, cancellationToken);
        }



        /// <summary>
        /// Read directory contents (files and subdirectories) asynchronously
        /// </summary>
        public virtual async Task<IEnumerable<FileObject>> ReadDirAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                // Normalize path first
                realPath = PathHelper.NormalizePath(realPath);

                // Throw if directory doesn't exist
                if (!_directoryContents.ContainsKey(realPath))
                {
                    throw new DirectoryNotFoundException($"Directory not found: {realPath}");
                }

                var entryList = new List<FileObject>();

                var result1 = _directoryContents[realPath];
                foreach (var item in result1)
                {
                    if (_memoryFiles.ContainsKey(item))
                    {
                        entryList.Add(_memoryFiles[item]);
                    }
                }

                _logger?.LogDebug("Enumerated {Count} entries in directory: {Path}", entryList.Count, realPath);
                return entryList.AsEnumerable();
            }, cancellationToken);
        }

        /// <summary>
        /// Test if storage is readable asynchronously
        /// </summary>
        public virtual async Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// Test if storage is writable asynchronously
        /// </summary>
        public virtual async Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
        {
            return await Task.FromResult(true);
        }

        /// <summary>
        /// Check if a file exists asynchronously
        /// </summary>
        public virtual async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                filePath = PathHelper.NormalizePath(filePath);
                bool exists = _memoryFiles.ContainsKey(filePath) && !_memoryFiles[filePath].IsDirectory;
                _logger?.LogTrace("File exists check for {Path}: {Exists}", filePath, exists);
                return exists;
            }, cancellationToken);
        }

        /// <summary>
        /// Check if a directory exists asynchronously
        /// </summary>
        public virtual async Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                directoryPath = PathHelper.NormalizePath(directoryPath);
                bool exists = _directoryContents.ContainsKey(directoryPath);
                _logger?.LogTrace("Directory exists check for {Path}: {Exists}", directoryPath, exists);
                return exists;
            }, cancellationToken);
        }

        /// <summary>
        /// Delete a directory asynchronously
        /// </summary>
        public virtual async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                path = PathHelper.NormalizePath(path);
                
                // Check if directory exists
                if (!_directoryContents.ContainsKey(path))
                {
                    throw new DirectoryNotFoundException($"Directory not found: {path}");
                }

                // Check if directory is empty
                if (_directoryContents[path].Count > 0)
                {
                    throw new IOException($"Directory not empty: {path}");
                }

                // Remove from directory contents
                _directoryContents.Remove(path);

                // Remove from memory files if it exists there
                _memoryFiles.Remove(path);

                // Remove from parent directory
                RemoveFromParentDirectory(path);
                
                _logger?.LogDebug("Deleted directory: {Path}", path);
            }, cancellationToken);
        }



        /// <summary>
        /// Get file information asynchronously
        /// </summary>
        public virtual async Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                filePath = PathHelper.NormalizePath(filePath);
                
                if (!_memoryFiles.ContainsKey(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }
                
                _logger?.LogTrace("Retrieved file info for: {Path}", filePath);
                return _memoryFiles[filePath];
            }, cancellationToken);
        }

        /// <summary>
        /// Delete a file asynchronously
        /// </summary>
        public virtual async Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                filePath = PathHelper.NormalizePath(filePath);
                
                if (!_memoryFiles.ContainsKey(filePath))
                {
                    throw new FileNotFoundException($"File not found: {filePath}");
                }

                // Remove from memory files
                _memoryFiles.Remove(filePath);

                // Remove from parent directory
                RemoveFromParentDirectory(filePath);
                
                _logger?.LogDebug("Deleted file: {Path}", filePath);
            }, cancellationToken);
        }

        /// <summary>
        /// Move/rename a file or directory asynchronously (unified method with auto-detection)
        /// Note: MemoryStorage doesn't need Windows FileShare.Delete fix since it doesn't use actual Windows file handles
        /// </summary>
        public virtual async Task MoveAsync(string sourcePath, string destinationPath, bool overwrite, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                sourcePath = PathHelper.NormalizePath(sourcePath);
                destinationPath = PathHelper.NormalizePath(destinationPath);
                
                // Auto-detect: Check what we're moving (directory takes precedence)
                bool isSourceDirectory = _directoryContents.ContainsKey(sourcePath);
                bool isSourceFile = !isSourceDirectory && _memoryFiles.ContainsKey(sourcePath) && !_memoryFiles[sourcePath].IsDirectory;
                
                if (!isSourceDirectory && !isSourceFile)
                {
                    throw new FileNotFoundException($"Source path not found: {sourcePath}");
                }
                
                if (isSourceDirectory)
                {
                    // Move directory
                    MoveDirectoryInternal(sourcePath, destinationPath, overwrite);
                    _logger?.LogDebug("Moved directory from {Source} to {Destination}", sourcePath, destinationPath);
                }
                else
                {
                    // Move file (existing logic)
                    MoveFileInternal(sourcePath, destinationPath, overwrite);
                    _logger?.LogDebug("Moved file from {Source} to {Destination}", sourcePath, destinationPath);
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Internal method to move a file in memory
        /// </summary>
        private void MoveFileInternal(string sourceFilePath, string destinationFilePath, bool overwrite)
        {
            if (!_memoryFiles.ContainsKey(sourceFilePath))
            {
                throw new FileNotFoundException($"Source file not found: {sourceFilePath}");
            }

            if (_memoryFiles.ContainsKey(destinationFilePath) && !overwrite)
            {
                throw new IOException($"Destination file exists: {destinationFilePath}");
            }

            // Get source file object
            var fileObject = _memoryFiles[sourceFilePath];

            // Update paths in the file object
            fileObject.RealPath = destinationFilePath;
            fileObject.VirtualPath = destinationFilePath;
            fileObject.Filename = System.IO.Path.GetFileName(destinationFilePath);

            // Move in memory files dictionary
            _memoryFiles.Remove(sourceFilePath);
            _memoryFiles[destinationFilePath] = fileObject;

            // Update parent directories
            RemoveFromParentDirectory(sourceFilePath);
            AddToParentDirectory(destinationFilePath);
        }

        /// <summary>
        /// Internal method to move a directory in memory (recursive)
        /// </summary>
        private void MoveDirectoryInternal(string sourceDirectoryPath, string destinationDirectoryPath, bool overwrite)
        {
            if (!_directoryContents.ContainsKey(sourceDirectoryPath))
            {
                throw new DirectoryNotFoundException($"Source directory not found: {sourceDirectoryPath}");
            }

            // Check if destination exists
            if (_directoryContents.ContainsKey(destinationDirectoryPath))
            {
                if (!overwrite)
                {
                    throw new IOException($"Destination directory already exists: {destinationDirectoryPath}");
                }
                // If overwrite is true, delete the destination directory recursively
                DeleteDirectoryRecursive(destinationDirectoryPath);
            }
            else if (_memoryFiles.ContainsKey(destinationDirectoryPath) && !_memoryFiles[destinationDirectoryPath].IsDirectory)
            {
                if (!overwrite)
                {
                    throw new IOException($"Destination file already exists: {destinationDirectoryPath}");
                }
                // Remove the file
                _memoryFiles.Remove(destinationDirectoryPath);
                RemoveFromParentDirectory(destinationDirectoryPath);
            }

            // Ensure parent directory of destination exists
            string? destParentDir = PathHelper.GetParentDirectory(destinationDirectoryPath);
            if (destParentDir != null && destParentDir != "/" && !_directoryContents.ContainsKey(destParentDir))
            {
                throw new DirectoryNotFoundException($"Parent directory does not exist: {destParentDir}");
            }

            // Get all files and subdirectories to move
            var itemsToMove = new List<string>(_directoryContents[sourceDirectoryPath]);
            
            // Create the destination directory first
            _directoryContents[destinationDirectoryPath] = new HashSet<string>();
            
            // Move the directory file object
            if (_memoryFiles.ContainsKey(sourceDirectoryPath))
            {
                var dirObject = _memoryFiles[sourceDirectoryPath];
                dirObject.RealPath = destinationDirectoryPath;
                dirObject.VirtualPath = destinationDirectoryPath;
                dirObject.Filename = System.IO.Path.GetFileName(destinationDirectoryPath);
                
                _memoryFiles.Remove(sourceDirectoryPath);
                _memoryFiles[destinationDirectoryPath] = dirObject;
            }

            // Recursively move all contents
            foreach (var item in itemsToMove)
            {
                string relativePath = item.Substring(sourceDirectoryPath.Length).TrimStart('/');
                string newItemPath = destinationDirectoryPath + "/" + relativePath;
                
                if (_directoryContents.ContainsKey(item))
                {
                    // It's a subdirectory - recursively move it
                    MoveDirectoryInternal(item, newItemPath, overwrite);
                }
                else if (_memoryFiles.ContainsKey(item))
                {
                    // It's a file - move it
                    MoveFileInternal(item, newItemPath, overwrite);
                }
            }

            // Remove the source directory
            _directoryContents.Remove(sourceDirectoryPath);
            
            // Update parent directory references
            RemoveFromParentDirectory(sourceDirectoryPath);
            AddToParentDirectory(destinationDirectoryPath);
        }

        /// <summary>
        /// Helper method to recursively delete a directory and all its contents
        /// </summary>
        private void DeleteDirectoryRecursive(string directoryPath)
        {
            if (!_directoryContents.ContainsKey(directoryPath))
                return;

            // Get all items in this directory
            var itemsToDelete = new List<string>(_directoryContents[directoryPath]);
            
            // Recursively delete all contents
            foreach (var item in itemsToDelete)
            {
                if (_directoryContents.ContainsKey(item))
                {
                    // It's a subdirectory - recursively delete it
                    DeleteDirectoryRecursive(item);
                }
                else if (_memoryFiles.ContainsKey(item))
                {
                    // It's a file - delete it
                    _memoryFiles.Remove(item);
                }
            }

            // Remove the directory itself
            _directoryContents.Remove(directoryPath);
            _memoryFiles.Remove(directoryPath);
            RemoveFromParentDirectory(directoryPath);
        }

        /// <summary>
        /// Create a directory asynchronously
        /// </summary>
        public virtual async Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                directoryPath = PathHelper.NormalizePath(directoryPath);
                
                // Check if directory already exists
                if (_directoryContents.ContainsKey(directoryPath))
                {
                    return; // Directory already exists
                }

                // Check if parent directory exists (unless it's root)
                if (directoryPath != "/")
                {
                    string? parentDir = PathHelper.GetParentDirectory(directoryPath);
                    if (parentDir != null && !_directoryContents.ContainsKey(parentDir))
                    {
                        throw new DirectoryNotFoundException($"Parent directory does not exist: {parentDir}");
                    }
                }

                // Create directory entry
                _directoryContents[directoryPath] = new HashSet<string>();

                // Create file object for the directory
                _memoryFiles[directoryPath] = new FileObject(directoryPath)
                {
                    Filename = System.IO.Path.GetFileName(directoryPath),
                    RealPath = directoryPath,
                    VirtualPath = directoryPath,
                    IsDirectory = true,
                    CreationTime = DateTime.Now,
                    LastModified = DateTime.Now,
                    LastAccessTime = DateTime.Now,
                    SC = this
                };

                // Add to parent directory if not root
                if (directoryPath != "/")
                {
                    AddToParentDirectory(directoryPath);
                }
                
                _logger?.LogDebug("Created directory: {Path}", directoryPath);
            }, cancellationToken);
        }

        /// <summary>
        /// Shutdown the storage connection asynchronously
        /// </summary>
        public virtual async Task ShutdownAsync(CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                lock (_handleLock)
                {
                    // Close all open handles
                    foreach (var handle in _handleMap.Values)
                    {
                        handle.Stream?.Dispose();
                    }
                    _handleMap.Clear();
                    
                    _logger?.LogDebug("MemoryStorage shutdown completed");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Get total capacity asynchronously (simulated for memory storage)
        /// </summary>
        public virtual async Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
        {
            // Return configured limit if set, otherwise unlimited
            if (LimitStorage > 0)
            {
                return await Task.FromResult(LimitStorage);
            }
            return await Task.FromResult(long.MaxValue); // Unlimited for memory storage
        }

        /// <summary>
        /// Get available free space asynchronously (simulated for memory storage)
        /// </summary>
        public virtual async Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
        {
            // If storage limit is set, calculate actual usage
            if (LimitStorage > 0)
            {
                return await Task.Run(() =>
                {
                    long usedSpace = 0;
                    
                    // Calculate used space from all files
                    foreach (var file in _memoryFiles.Values)
                    {
                        if (!file.IsDirectory)
                        {
                            usedSpace += file.Size;
                        }
                    }
                    
                    // Return available space (limit - used), but not less than 0
                    long availableSpace = LimitStorage - usedSpace;
                    return Math.Max(0, availableSpace);
                }, cancellationToken);
            }
            
            return await Task.FromResult(long.MaxValue); // Unlimited for memory storage
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
        /// Change file ownership asynchronously (no-op for memory storage)
        /// </summary>
        public virtual async Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                path = PathHelper.NormalizePath(path);
                _logger?.LogDebug("Chown operation (no-op) for {Path} (UID={Uid}, GID={Gid})", path, uid, gid);
            }, cancellationToken);
        }

        /// <summary>
        /// Set file times asynchronously
        /// </summary>
        public virtual async Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default)
        {
            await Task.Run(() =>
            {
                path = PathHelper.NormalizePath(path);
                
                if (_memoryFiles.TryGetValue(path, out var fileObject))
                {
                    fileObject.LastAccessTime = accessTime;
                    fileObject.LastModified = modifiedTime;
                    
                    _logger?.LogDebug("Updated times for {Path}", path);
                }
                else
                {
                    throw new FileNotFoundException($"File not found: {path}");
                }
            }, cancellationToken);
        }

        /// <summary>
        /// Add a file/directory to its parent directory's contents
        /// </summary>
        private void AddToParentDirectory(string path)
        {
            string? parentDir = PathHelper.GetParentDirectory(path);
            if (parentDir != null)
            {
                if (!_directoryContents.ContainsKey(parentDir))
                {
                    _directoryContents[parentDir] = new HashSet<string>();
                }
                _directoryContents[parentDir].Add(path);
            }
        }

        /// <summary>
        /// Remove a file/directory from its parent directory's contents
        /// </summary>
        private void RemoveFromParentDirectory(string path)
        {
            string? parentDir = PathHelper.GetParentDirectory(path);
            if (parentDir != null && _directoryContents.ContainsKey(parentDir))
            {
                _directoryContents[parentDir].Remove(path);
            }
        }

        /// <summary>
        /// Create parent directory chain in memory (like mkdir -p)
        /// </summary>
        private void CreateParentDirectoriesInMemory(string filePath)
        {
            var parts = filePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return; // Root path
            
            // Build directory chain: /a, /a/b, /a/b/c, etc.
            string currentPath = "";
            for (int i = 0; i < parts.Length - 1; i++) // Exclude filename
            {
                currentPath += "/" + parts[i];
                
                // Create directory if it doesn't exist
                if (!_directoryContents.ContainsKey(currentPath))
                {
                    _directoryContents[currentPath] = new HashSet<string>();
                    
                    // Create directory file object
                    _memoryFiles[currentPath] = new FileObject(currentPath)
                    {
                        Filename = parts[i],
                        RealPath = currentPath,
                        VirtualPath = currentPath,
                        IsDirectory = true,
                        CreationTime = DateTime.Now,
                        LastModified = DateTime.Now,
                        LastAccessTime = DateTime.Now,
                        SC = this
                    };
                    
                    // Add to parent directory (if not root)
                    if (i > 0)
                    {
                        AddToParentDirectory(currentPath);
                    }
                    
                    _logger?.LogTrace("Created in-memory directory: {Path}", currentPath);
                }
            }
        }

        public virtual async Task TruncateAsync(string path, IntPtr fileHandle, long size, CancellationToken cancellationToken = default)
        {
            // FileHandle is always valid in our system - FUSE gives us back our own handles
            if (!_handleMap.TryGetValue(fileHandle, out var handle))
                throw new InvalidOperationException($"Invalid file handle: {fileHandle}");

            try
            {
                long currentLength = handle.Stream.Length;
                handle.Stream.SetLength(size);
                
                // MemoryStream automatically zero-fills when expanding
                // No manual null byte filling needed for MemoryStream
                
                // Update the file object with the new data
                if (handle.Stream is MemoryStream memStream && _memoryFiles.TryGetValue(handle.Path, out var fileObject))
                {
                    fileObject.Bytes = memStream.ToArray();
                    fileObject.Size = fileObject.Bytes.Length;
                    fileObject.LastModified = DateTime.Now;
                    fileObject.LastAccessTime = DateTime.Now;
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
            
            // Task.CompletedTask for async signature (no actual async work in memory)
            await Task.CompletedTask;
        }

        /// <summary>
        /// Dispose of resources
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_handleLock)
                {
                    foreach (var handle in _handleMap.Values)
                    {
                        handle.Stream?.Dispose();
                    }
                    _handleMap.Clear();
                }
                _memoryFiles.Clear();
                _directoryContents.Clear();
                
                _logger?.LogDebug("MemoryStorage disposed");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
} 