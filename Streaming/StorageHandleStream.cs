using System.Runtime.InteropServices;
using StorageLib.Abstractions;

namespace StorageLib.Streaming
{
    /// <summary>
    /// Translates IStorage handle operations to Stream interface.
    /// Handles all IntPtr ↔ Stream conversions automatically.
    /// This is the core translator that makes the adapter pattern work.
    /// </summary>
    internal class StorageHandleStream : Stream
    {
        private readonly IStorage _storage;
        private readonly IntPtr _handle;
        private readonly string _path;
        private readonly FileAccess _access;
        private long _position;
        private bool _disposed;
        private long? _cachedLength;
        
        public StorageHandleStream(IStorage storage, IntPtr handle, string path, FileAccess access)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _handle = handle;
            _path = path ?? throw new ArgumentNullException(nameof(path));
            _access = access;
            _position = 0;
        }
        
        #region Stream Properties
        
        public override bool CanRead => _access.HasFlag(FileAccess.Read) && !_disposed;
        public override bool CanWrite => _access.HasFlag(FileAccess.Write) && !_disposed;
        public override bool CanSeek => !_disposed;
        
        public override long Length
        {
            get
            {
                ThrowIfDisposed();
                return GetLengthInternal();
            }
        }
        
        public override long Position
        {
            get
            {
                ThrowIfDisposed();
                return _position;
            }
            set
            {
                ThrowIfDisposed();
                _position = Math.Max(0, value);
            }
        }
        
        #endregion
        
        #region Core Stream Operations
        
        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!CanRead)
                throw new NotSupportedException("Stream not opened for reading");
            
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return 0;
            
            // Check if we're at end of file
            var fileLength = Length;
            if (_position >= fileLength)
                return 0;
            
            // Adjust count if we would read past end of file
            var actualCount = (int)Math.Min(count, fileLength - _position);
            if (actualCount <= 0)
                return 0;
            
            var dataPtr = Marshal.AllocHGlobal(actualCount);
            try
            {
                await _storage.ReadAsync(_path, _handle, _position, actualCount, dataPtr, cancellationToken);
                Marshal.Copy(dataPtr, buffer, offset, actualCount);
                _position += actualCount;
                return actualCount;
            }
            finally
            {
                Marshal.FreeHGlobal(dataPtr);
            }
        }
        
        public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!CanWrite)
                throw new NotSupportedException("Stream not opened for writing");
            
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));
            if (offset < 0 || offset > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));
            if (count < 0 || offset + count > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(count));
            if (count == 0)
                return;
            
            var dataPtr = Marshal.AllocHGlobal(count);
            try
            {
                Marshal.Copy(buffer, offset, dataPtr, count);
                await _storage.WriteAsync(_path, _handle, _position, count, dataPtr, cancellationToken);
                _position += count;
                
                // Invalidate cached length since we wrote data
                _cachedLength = null;
            }
            finally
            {
                Marshal.FreeHGlobal(dataPtr);
            }
        }
        
        public override long Seek(long offset, SeekOrigin origin)
        {
            ThrowIfDisposed();
            
            _position = origin switch
            {
                SeekOrigin.Begin => Math.Max(0, offset),
                SeekOrigin.Current => Math.Max(0, _position + offset),
                SeekOrigin.End => Math.Max(0, Length + offset),
                _ => throw new ArgumentException("Invalid seek origin", nameof(origin))
            };
            
            return _position;
        }
        
        public override void Flush()
        {
            ThrowIfDisposed();
            // IStorage doesn't have explicit flush, operations are generally immediate
        }
        
        public override Task FlushAsync(CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            // IStorage doesn't have explicit flush, operations are generally immediate
            return Task.CompletedTask;
        }
        
        #endregion
        
        #region Synchronous Wrappers (required by Stream base class)
        
        public override int Read(byte[] buffer, int offset, int count)
        {
            return ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }
        
        public override void Write(byte[] buffer, int offset, int count)
        {
            WriteAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();
        }
        
        public override void SetLength(long value)
        {
            throw new NotSupportedException("SetLength is not supported by StorageHandleStream");
        }
        
        #endregion
        
        #region Helper Methods
        
        private long GetLengthInternal()
        {
            if (_cachedLength.HasValue)
                return _cachedLength.Value;
            
            try
            {
                // Try to get file info to determine length
                var fileInfo = _storage.GetFileInfoAsync(_path, CancellationToken.None).GetAwaiter().GetResult();
                _cachedLength = fileInfo?.Size ?? 0;
                return _cachedLength.Value;
            }
            catch
            {
                // If we can't get file info, assume 0 length
                // This might happen for write-only streams or special files
                return 0;
            }
        }
        
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(StorageHandleStream));
        }
        
        #endregion
        
        #region Disposal
        
        protected override void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Close the storage handle
                    _storage.CloseAsync(_path, _handle, CancellationToken.None).GetAwaiter().GetResult();
                }
                catch
                {
                    // Swallow exceptions during disposal to prevent issues
                    // The underlying storage should handle cleanup gracefully
                }
                finally
                {
                    _disposed = true;
                }
            }
            
            base.Dispose(disposing);
        }
        
        public override async ValueTask DisposeAsync()
        {
            if (!_disposed)
            {
                try
                {
                    await _storage.CloseAsync(_path, _handle, CancellationToken.None);
                }
                catch
                {
                    // Swallow exceptions during disposal
                }
                finally
                {
                    _disposed = true;
                }
            }
            
            await base.DisposeAsync();
        }
        
        #endregion
    }
} 