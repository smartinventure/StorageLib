using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace StorageLib.OpenSlide.Extensions
{
    internal sealed class UnmanagedTypeCastMemoryManager<TFrom, TTo> : MemoryManager<TTo>
        where TFrom : unmanaged
        where TTo : unmanaged
    {
        private readonly Memory<TFrom> _memory;

        public UnmanagedTypeCastMemoryManager(Memory<TFrom> memory)
        {
            _memory = memory;
        }

        public override Span<TTo> GetSpan()
        {
            return MemoryMarshal.Cast<byte, TTo>(MemoryMarshal.AsBytes(_memory.Span));
        }

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            return _memory.Slice(elementIndex * Unsafe.SizeOf<TTo>() / Unsafe.SizeOf<TFrom>()).Pin();
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}
