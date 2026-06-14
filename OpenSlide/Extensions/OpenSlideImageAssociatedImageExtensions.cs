using PooledGrowableBufferHelper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace StorageLib.OpenSlide.Extensions
{
    public static class OpenSlideImageAssociatedImageExtensions
    {
        public static byte[] GetAssociatedImageAsJpeg(this OpenSlideImage image, string name, int quality = 85)
        {
            using var buffer = ReadImage(image, name);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            using var bufferImage = buffer.GetImage();
            bufferImage.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });

            return ms.ToArray();
        }

        public static async Task<byte[]> GetAssociatedImageAsJpegAsync(this OpenSlideImage image, string name, int quality = 85)
        {
            using var buffer = ReadImage(image, name);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            using var bufferImage = buffer.GetImage();
            await bufferImage.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality });

            return ms.ToArray();
        }

        public static void GetAssociatedImageAsJpegToStream(this OpenSlideImage image, string name, Stream stream, int quality = 85)
        {
            using var buffer = ReadImage(image, name);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            using var bufferImage = buffer.GetImage();
            bufferImage.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });

            ms.Seek(0, SeekOrigin.Begin);
            ms.CopyTo(stream);
        }

        public static void GetAssociatedImageAsJpegToStream(this OpenSlideImage image, string name, MemoryStream stream, int quality = 85)
        {
            using var buffer = ReadImage(image, name);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            using var bufferImage = buffer.GetImage();
            bufferImage.SaveAsJpeg(stream, new JpegEncoder { Quality = quality });
        }

        public static async Task GetAssociatedImageAsJpegToStreamAsync(this OpenSlideImage image, string name, Stream stream, int quality = 85, CancellationToken cancellationToken = default)
        {
            using var buffer = ReadImage(image, name);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            using var bufferImage = buffer.GetImage();
            bufferImage.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });

            ms.Seek(0, SeekOrigin.Begin);
            await ms.CopyToAsync(stream, 4096, cancellationToken).ConfigureAwait(false);
        }

        private static Bgra32ImageBuffer ReadImage(OpenSlideImage image, string name)
        {
            if (!image.TryGetAssociatedImageDimensions(name, out var dims))
            {
                throw new KeyNotFoundException();
            }

            return image.ReadAssociatedImageBuffer(name, dims.Width, dims.Height);
        }
    }
}
