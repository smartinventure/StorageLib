using System.Runtime.InteropServices;
using System.Text;
using StorageLib.Abstractions;

namespace StorageLib.Streaming
{
    /// <summary>
    /// Universal adapter that converts any IStorage to IStreamStorage.
    /// Handles IntPtr ↔ Stream translation automatically using the adapter pattern.
    /// </summary>
    public class StreamStorageAdapter : IStreamStorage
    {
        private readonly IStorage _storage;
        
        public IStorage UnderlyingStorage => _storage;
        
        public StreamStorageAdapter(IStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
        }
        
        #region Core Stream Operations
        
        public async Task<Stream> OpenReadAsync(string path, CancellationToken cancellationToken = default)
        {
            var handle = await _storage.OpenAsync(path, OpenFlags.ReadOnly, cancellationToken);
            return new StorageHandleStream(_storage, handle, path, FileAccess.Read);
        }
        
        public async Task<Stream> OpenWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            var handle = await _storage.OpenAsync(path, OpenFlags.WriteOnly | OpenFlags.Create, cancellationToken);
            return new StorageHandleStream(_storage, handle, path, FileAccess.Write);
        }
        
        public async Task<Stream> OpenReadWriteAsync(string path, CancellationToken cancellationToken = default)
        {
            var handle = await _storage.OpenAsync(path, OpenFlags.ReadWrite | OpenFlags.Create, cancellationToken);
            return new StorageHandleStream(_storage, handle, path, FileAccess.ReadWrite);
        }
        
        public async Task<Stream> OpenAsync(string path, OpenFlags flags, CancellationToken cancellationToken = default)
        {
            var handle = await _storage.OpenAsync(path, flags, cancellationToken);
            
            // Determine FileAccess from OpenFlags
            var accessMode = FuseFlags.GetAccessMode(flags);
            var fileAccess = accessMode switch
            {
                OpenFlags.ReadOnly => FileAccess.Read,
                OpenFlags.WriteOnly => FileAccess.Write,
                OpenFlags.ReadWrite => FileAccess.ReadWrite,
                _ => FileAccess.Read
            };
            
            return new StorageHandleStream(_storage, handle, path, fileAccess);
        }
        
        #endregion
        
        #region Convenience Methods
        
        public async Task<byte[]> ReadAllBytesAsync(string path, CancellationToken cancellationToken = default)
        {
            using var stream = await OpenReadAsync(path, cancellationToken);
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, cancellationToken);
            return memoryStream.ToArray();
        }
        
        public async Task WriteAllBytesAsync(string path, byte[] data, CancellationToken cancellationToken = default)
        {
            using var stream = await OpenWriteAsync(path, cancellationToken);
            await stream.WriteAsync(data, 0, data.Length, cancellationToken);
        }
        
        public async Task<string> ReadAllTextAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            var bytes = await ReadAllBytesAsync(path, cancellationToken);
            return encoding.GetString(bytes);
        }
        
        public async Task WriteAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            var bytes = encoding.GetBytes(content);
            await WriteAllBytesAsync(path, bytes, cancellationToken);
        }
        
        public async Task<string[]> ReadAllLinesAsync(string path, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            var content = await ReadAllTextAsync(path, encoding, cancellationToken);
            return content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
        }
        
        public async Task WriteAllLinesAsync(string path, IEnumerable<string> lines, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            var content = string.Join(Environment.NewLine, lines);
            await WriteAllTextAsync(path, content, encoding, cancellationToken);
        }
        
        public async Task AppendAllTextAsync(string path, string content, Encoding? encoding = null, CancellationToken cancellationToken = default)
        {
            encoding ??= Encoding.UTF8;
            var bytes = encoding.GetBytes(content);
            
            // Open with append flag
            var handle = await _storage.OpenAsync(path, OpenFlags.WriteOnly | OpenFlags.Create | OpenFlags.Append, cancellationToken);
            try
            {
                using var stream = new StorageHandleStream(_storage, handle, path, FileAccess.Write);
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
            }
            catch
            {
                // Ensure handle is closed even if stream creation fails
                await _storage.CloseAsync(path, handle, cancellationToken);
                throw;
            }
        }
        
        public async Task CopyFromStreamAsync(string destinationPath, Stream sourceStream, CancellationToken cancellationToken = default)
        {
            using var destinationStream = await OpenWriteAsync(destinationPath, cancellationToken);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }
        
        public async Task CopyToStreamAsync(string sourcePath, Stream destinationStream, CancellationToken cancellationToken = default)
        {
            using var sourceStream = await OpenReadAsync(sourcePath, cancellationToken);
            await sourceStream.CopyToAsync(destinationStream, cancellationToken);
        }
        
        #endregion
        
        #region Delegate Methods (Pass-through to IStorage)
        
        public Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
            => _storage.FileExistsAsync(filePath, cancellationToken);
            
        public Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
            => _storage.DirectoryExistsAsync(directoryPath, cancellationToken);
            
        public Task CreateDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
            => _storage.CreateDirectoryAsync(directoryPath, cancellationToken);
            
        public Task DeleteAsync(string filePath, CancellationToken cancellationToken = default)
            => _storage.DeleteAsync(filePath, cancellationToken);
            
        public Task DeleteDirectoryAsync(string directoryPath, CancellationToken cancellationToken = default)
            => _storage.DeleteDirectoryAsync(directoryPath, cancellationToken);
            
        public Task MoveAsync(string sourceFilePath, string destinationFilePath, bool overwrite, CancellationToken cancellationToken = default)
            => _storage.MoveAsync(sourceFilePath, destinationFilePath, overwrite, cancellationToken);
            
        public Task<IEnumerable<FileObject>> ReadDirAsync(string directoryPath, bool readOnly = true, CancellationToken cancellationToken = default)
            => _storage.ReadDirAsync(directoryPath, readOnly, cancellationToken);
            
        public Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
            => _storage.GetFileInfoAsync(filePath, cancellationToken);
            
        public Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default)
            => _storage.ChownAsync(path, uid, gid, cancellationToken);
            
        public Task SetTimesAsync(string path, DateTime accessTime, DateTime modifiedTime, CancellationToken cancellationToken = default)
            => _storage.SetTimesAsync(path, accessTime, modifiedTime, cancellationToken);
            
        public Task<bool> TestReadAsync(CancellationToken cancellationToken = default)
            => _storage.TestReadAsync(cancellationToken);
            
        public Task<bool> TestWriteAsync(CancellationToken cancellationToken = default)
            => _storage.TestWriteAsync(cancellationToken);
            
        public Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
            => _storage.GetTotalCapacityAsync(cancellationToken);
            
        public Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
            => _storage.GetAvailableFreeSpaceAsync(cancellationToken);
            
        #endregion
    }
} 