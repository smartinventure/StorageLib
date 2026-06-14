using System.Buffers;
using System.Runtime.CompilerServices;

namespace StorageLib.OpenSlide.Extensions;

internal static class ValueTypeCastMemoryManagerExtensions
{
    public static Memory<TTo> Cast<TFrom, TTo>(this MemoryManager<TFrom> memoryManager)
        where TFrom : unmanaged
        where TTo : unmanaged
    {
        if (typeof(TFrom) == typeof(TTo))
        {
            return Unsafe.As<MemoryManager<TTo>>(memoryManager).Memory;
        }

        return new UnmanagedTypeCastMemoryManager<TFrom, TTo>(memoryManager.Memory).Memory;
    }

    public static Memory<TTo> Cast<TFrom, TTo>(this Memory<TFrom> memory)
        where TFrom : unmanaged
        where TTo : unmanaged
    {
        if (typeof(TFrom) == typeof(TTo))
        {
            return Unsafe.As<Memory<TFrom>, Memory<TTo>>(ref memory);
        }

        return new UnmanagedTypeCastMemoryManager<TFrom, TTo>(memory).Memory;
    }
}