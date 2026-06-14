using System.Text;
using StorageLib.Abstractions;

namespace StorageLib.Streaming
{
    /// <summary>
    /// Stream-based storage interface providing .NET-idiomatic access to storage operations.
    /// Implemented via adapter pattern over IStorage - no duplication of core logic.
    /// </summary>
    public interface IStreamStorage
    {
        /// <summary>
        /// Gets the underlying IStorage instance that this stream storage wraps
        /// </summary>
        IStorage UnderlyingStorage { get; }

        // Core stream operations
        /// <summary>
        /// Opens a file for reading as a Stream
        /// </summary>
        Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Opens a file for writing as a Stream
        /// </summary>
        Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Opens a file for both reading and writing as a Stream
        /// </summary>
        Task<Stream> OpenReadWriteAsync(string path, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Opens a file with specific flags as a Stream
        /// </summary>
        Task<Stream> OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken = default);
        
        // Convenience methods for common operations
        /// <summary>
        /// Reads all bytes from a file
        /// </summary>
        Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Writes all bytes to a file
        /// </summary>
        Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Reads all text from a file with optional encoding
        /// </summary>
        Task<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Writes all text to a file with optional encoding
        /// </summary>
        Task WriteAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Reads all lines from a text file
        /// </summary>
        Task<string[]> ReadAllLinesAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Writes all lines to a text file
        /// </summary>
        Task WriteAllLinesAsync(string path, IEnumerable<string> lines, Encoding? encoding = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Appends text to a file
        /// </summary>
        Task AppendAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Copies data from one stream to another through the storage system
        /// </summary>
        Task CopyFromStreamAsync(string destinationPath, Stream sourceStream, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Copies a file to a stream
        /// </summary>
        Task CopyToStreamAsync(string sourcePath, Stream destinationStream, CancellationToken cancellationToken = default);
        
        // Delegate methods - pass through to underlying IStorage (no translation needed)
        /// <summary>
        /// Checks if a file exists
        /// </summary>
        Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Checks if a directory exists
        /// </summary>
        Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Creates a directory
        /// </summary>
        Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Deletes a file
        /// </summary>
        Task DeleteAsync(string filePath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Deletes a directory
        /// </summary>
        Task DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Moves a file
        /// </summary>
        Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Reads directory contents
        /// </summary>
        Task<IEnumerable<FileObject>> ReadDirAsync(string directoryPath, bool readOnly = true, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets file information
        /// </summary>
        Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Changes file ownership
        /// </summary>
        Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Sets file timestamps
        /// </summary>
        Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Tests read access
        /// </summary>
        Task<bool> TestReadAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Tests write access
        /// </summary>
        Task<bool> TestWriteAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets total storage capacity
        /// </summary>
        Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Gets available free space
        /// </summary>
        Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default);
    }
} 