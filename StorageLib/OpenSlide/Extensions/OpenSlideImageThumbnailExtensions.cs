using PooledGrowableBufferHelper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace StorageLib.OpenSlide.Extensions
{
    public static class OpenSlideImageThumbnailExtensions
    {
        private const int DefaultQuality = 75;

        public static byte[] GetThumbnailAsJpeg(this OpenSlideImage image, int maxSize, int quality = DefaultQuality)
        {
            using var thumbnail = GenerateThumbnail(image, maxSize, maxSize);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            thumbnail.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });

            return ms.ToArray();
        }

        public static void GetThumbnailAsJpegToStream(this OpenSlideImage image, int maxSize, Stream stream, int quality = DefaultQuality)
        {
            using var thumbnail = GenerateThumbnail(image, maxSize, maxSize);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            thumbnail.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });

            ms.Seek(0, SeekOrigin.Begin);
            ms.CopyTo(stream);
        }

        public static void GetThumbnailAsJpegToStream(this OpenSlideImage image, int maxSize, MemoryStream stream, int quality = DefaultQuality)
        {
            using var thumbnail = GenerateThumbnail(image, maxSize, maxSize);
            thumbnail.SaveAsJpeg(stream, new JpegEncoder { Quality = quality });
        }

        public static async Task GetThumbnailAsJpegToStreamAsync(this OpenSlideImage image, int maxSize, Stream stream, int quality = DefaultQuality, CancellationToken cancellationToken = default)
        {
            using var thumbnail = GenerateThumbnail(image, maxSize, maxSize);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            await thumbnail.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality });

            ms.Seek(0, SeekOrigin.Begin);
            await ms.CopyToAsync(stream, 4096, cancellationToken).ConfigureAwait(false);
        }

        private static Image<Bgra32> GenerateThumbnail(OpenSlideImage image, int maxWidth, int maxHeight)
        {
            ArgumentNullException.ThrowIfNull(image);

            (long width, long height) = image.Dimensions;

            // Find the appropriate level
            double downsampleWidth = width / (double)maxWidth;
            double downsampleHeight = height / (double)maxHeight;
            double downsample = Math.Max(downsampleWidth, downsampleHeight);
            int level = image.GetBestLevelForDownsample(downsample);
            (long levelWidth, long levelHeight) = image.GetLevelDimensions(level);

            // Calculate target size
            int targetWidth, targetHeight;
            if (downsampleWidth > downsampleHeight)
            {
                targetWidth = maxWidth;
                targetHeight = (int)(height / downsampleWidth);
            }
            else
            {
                targetWidth = (int)(width / downsampleHeight);
                targetHeight = maxHeight;
            }

            using Bgra32ImageBuffer buffer = image.ReadImageBuffer(level, 0, 0, levelWidth, levelHeight);
            using var bufferImage = buffer.GetImage();

            return bufferImage.Clone(x => x.Resize(new ResizeOptions
            {
                Size = new Size(targetWidth, targetHeight)
            }));
        }
    }
}
