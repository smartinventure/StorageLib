using System.Buffers;

namespace StorageLib.Utilities
{
    /// <summary>
    /// Exposes a caller-owned native buffer as <see cref="Memory{T}"/>, so streams can read into it and
    /// write from it directly. The caller keeps the buffer alive and pinned for as long as the memory is used.
    /// </summary>
    internal sealed unsafe class NativeBufferMemoryManager : MemoryManager<byte>
    {
        private readonly IntPtr _buffer;
        private readonly int _length;

        public NativeBufferMemoryManager(IntPtr buffer, int length)
        {
            if (buffer == IntPtr.Zero)
                throw new ArgumentException("Buffer cannot be null", nameof(buffer));
            ArgumentOutOfRangeException.ThrowIfNegative(length);

            _buffer = buffer;
            _length = length;
        }

        public override Span<byte> GetSpan() => new Span<byte>((void*)_buffer, _length);

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(elementIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(elementIndex, _length);
            return new MemoryHandle((byte*)_buffer + elementIndex);
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
