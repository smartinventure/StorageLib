namespace StorageLib.Abstractions
{
    /// <summary>
    /// Interface for storage backends that provide file system operations.
    /// </summary>
    public interface IStorage : IDisposable
    {
        // Properties
        EnumStorageType Type { get; set; }
        string BaseFolderOrContainer { get; set; }
        string ConnectionString { get; set; }

        /// <summary>
        /// Maximum read speed in bytes per second. 0 means no throttling.
        /// </summary>
        long MaxReadBytesPerSecond { get; set; }

        /// <summary>
        /// Maximum write speed in bytes per second. 0 means no throttling.
        /// </summary>
        long MaxWriteBytesPerSecond { get; set; }

        /// <summary>
        /// Initialize or set up the storage connection asynchronously
        /// </summary>
        Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Open a file asynchronously with the specified flags
        /// </summary>
        Task<IntPtr> OpenAsync(string realPath, OpenFlags flags, CancellationToken cancellationToken = default);

        /// <summary>
        /// Close a file asynchronously
        /// </summary>
        Task CloseAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default);

        /// <summary>
        /// Read data from a file asynchronously.
        /// Returns the number of bytes actually read (which may be less than <paramref name="size"/>
        /// at end-of-file). Callers MUST honor this count and not assume the full requested size.
        /// </summary>
        Task<int> ReadAsync(string path, IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default);

        /// <summary>
        /// Write data to a file asynchronously.
        /// Returns the number of bytes actually written.
        /// </summary>
        Task<int> WriteAsync(string path, IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default);

        
        /// <summary>
        /// Read directory contents (files and subdirectories) asynchronously.
        /// This is the primary method used by the layer system for directory operations.
        /// </summary>
        Task<IEnumerable<FileObject>> ReadDirAsync(string realPath, bool readOnly = true, CancellationToken cancellationToken = default);

        /// <summary>
        /// Test read access to the storage asynchronously
        /// </summary>
        Task<bool> TestReadAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Test write access to the storage asynchronously
        /// </summary>
        Task<bool> TestWriteAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if a file exists asynchronously
        /// </summary>
        Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if a directory exists asynchronously
        /// </summary>
        Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Delete a directory asynchronously
        /// </summary>
        Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get file information asynchronously
        /// </summary>
        Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// The file or directory at <paramref name="filePath"/>, or null when there is none. Programs ask for
        /// names that do not exist all the time, and an exception per question costs far more than the question;
        /// storages that can answer without one override this.
        /// </summary>
        async Task<FileObject?> TryGetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            try
            {
                return await GetFileInfoAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Delete a file asynchronously
        /// </summary>
        Task DeleteAsync(string filePath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Move a file asynchronously
        /// </summary>
        Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default);

        /// <summary>
        /// Create a directory asynchronously
        /// </summary>
        Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);

        /// <summary>
        /// Shutdown the storage connection asynchronously
        /// </summary>
        Task ShutdownAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the total storage capacity asynchronously
        /// </summary>
        Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the available free space asynchronously
        /// </summary>
        Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Close all open file handles asynchronously
        /// </summary>
        Task CloseAllHandlesAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Change file ownership asynchronously
        /// </summary>
        Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Set file timestamps asynchronously
        /// </summary>
        Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default);

        /// <summary>
        /// Flush pending data for a file handle to storage asynchronously
        /// </summary>
        Task FlushAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default);

        /// <summary>
        /// Like <see cref="FlushAsync"/>, but returns only once the data is on the storage's disk, as fsync does.
        /// Storages without such a guarantee to give just flush.
        /// </summary>
        Task FlushToDiskAsync(string path, IntPtr fileHandle, CancellationToken cancellationToken = default) =>
            FlushAsync(path, fileHandle, cancellationToken);

        /// <summary>
        /// Allocate space for a file handle asynchronously (pre-allocation for performance)
        /// </summary>
        Task FAllocateAsync(string path, IntPtr fileHandle, int mode, long offset, long length, CancellationToken cancellationToken = default);

        /// <summary>
        /// Truncate (set the size of) a file asynchronously
        /// If new size is larger than old size, the added space is filled with null bytes
        /// FileContext may be IntPtr.Zero on non-Windows systems
        /// </summary>
        Task TruncateAsync(string path, IntPtr fileHandle, long size, CancellationToken cancellationToken = default);
    }
} 