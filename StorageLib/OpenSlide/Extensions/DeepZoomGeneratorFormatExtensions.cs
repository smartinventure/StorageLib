using PooledGrowableBufferHelper;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace StorageLib.OpenSlide.Extensions
{
    public static class DeepZoomGeneratorFormatExtensions
    {
        public static byte[] GetTileAsJpeg(this DeepZoomGenerator dz, int level, int locationX, int locationY, out TileInfo info, int quality = 85)
        {
            ArgumentNullException.ThrowIfNull(dz);

            info = dz.GetTileInfo(level, locationX, locationY);

            using var image = ReadImage(dz, info);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            image.Mutate(x => x.BackgroundColor(Color.White));
            image.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });

            return ms.ToArray();
        }

        public static async Task<byte[]> GetTileAsJpegAsync(this DeepZoomGenerator dz, int level, int locationX, int locationY, int quality = 85)
        {
            ArgumentNullException.ThrowIfNull(dz);

            var info = dz.GetTileInfo(level, locationX, locationY);

            using var image = ReadImage(dz, info);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            image.Mutate(x => x.BackgroundColor(Color.White).Brightness(1));

            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality });
            return ms.ToArray();
        }

        public static void GetTileAsJpegToStream(this DeepZoomGenerator dz, int level, int locationX, int locationY, Stream stream, int quality = 85)
        {
            ArgumentNullException.ThrowIfNull(dz);

            var info = dz.GetTileInfo(level, locationX, locationY);

            using var image = ReadImage(dz, info);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            image.SaveAsJpeg(ms, new JpegEncoder { Quality = quality });

            ms.Seek(0, SeekOrigin.Begin);
            ms.CopyTo(stream);
        }

        public static void GetTileAsJpegToStream(this DeepZoomGenerator dz, int level, int locationX, int locationY, MemoryStream stream, int quality = 85)
        {
            ArgumentNullException.ThrowIfNull(dz);

            var info = dz.GetTileInfo(level, locationX, locationY);

            using var image = ReadImage(dz, info);

            image.SaveAsJpeg(stream, new JpegEncoder { Quality = quality });
        }

        public static async Task GetTileAsJpegToStreamAsync(this DeepZoomGenerator dz, int level, int locationX, int locationY, Stream stream, int quality = 75, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(dz);

            var info = dz.GetTileInfo(level, locationX, locationY);

            using var image = ReadImage(dz, info);
            using var ms = PooledMemoryStreamManager.Shared.GetStream();

            await image.SaveAsJpegAsync(ms, new JpegEncoder { Quality = quality });

            ms.Seek(0, SeekOrigin.Begin);
            await ms.CopyToAsync(stream, 4096, cancellationToken).ConfigureAwait(false);
        }

        private static Image<Bgra32> ReadImage(DeepZoomGenerator src, TileInfo tileInfo)
        {
            using var buffer = src.Image.ReadImageBuffer(tileInfo.SlideLevel, tileInfo.X, tileInfo.Y, tileInfo.Width, tileInfo.Height);
            if (tileInfo.Width != tileInfo.TileWidth || tileInfo.Height != tileInfo.TileHeight)
            {
                using var bufferImage = buffer.GetImage();

                return bufferImage.Clone(ctx =>
                {
                    ctx.Resize(tileInfo.TileWidth, tileInfo.TileHeight);
                });
            }

            return buffer.GetImage().Clone();
        }
    }
}
