using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Buffers;

namespace StorageLib.OpenSlide.Extensions;

internal readonly struct Bgra32ImageBuffer : IDisposable
{
    private readonly byte[] _buffer;
    private readonly Image<Bgra32> _image;

    public Bgra32ImageBuffer(int width, int height)
    {
        if (width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        if (height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(height));
        }

        _buffer = ArrayPool<byte>.Shared.Rent(width * height * 4);
        _image = Image.WrapMemory(_buffer.AsMemory().Cast<byte, Bgra32>().Slice(0, width * height), width, height);
    }

    public ref byte GetPinnableReference()
    {
        return ref _buffer[0];
    }

    public Image<Bgra32> GetImage() => _image;

    public void Dispose()
    {
        ArrayPool<byte>.Shared.Return(_buffer);
        _image.Dispose();
    }
}