using Azure.Storage.Blobs;

namespace StorageLib.Connectors
{
    public class BlobWriteStream : Stream
    {
        private readonly BlobClient _blobClient;
        private readonly MemoryStream _stream;

        public BlobWriteStream(BlobClient blobClient, MemoryStream stream)
        {
            _blobClient = blobClient;
            _stream = stream;
        }

        public override void Flush()
        {
            _stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return _stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return _stream.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            _stream.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            _stream.Write(buffer, offset, count);
        }

        public override bool CanRead => _stream.CanRead;
        public override bool CanSeek => _stream.CanSeek;
        public override bool CanWrite => _stream.CanWrite;
        public override long Length => _stream.Length;
        public override long Position
        {
            get => _stream.Position;
            set => _stream.Position = value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _stream.Position = 0;
                _blobClient.Upload(_stream, true);
                _stream.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

