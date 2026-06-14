namespace StorageLib.OpenSlide.Extensions
{
    internal static class OpenSlideImageExtensions
    {
        public static Bgra32ImageBuffer ReadImageBuffer(this OpenSlideImage image, int level, long x, long y, long w, long h)
        {
            var buffer = new Bgra32ImageBuffer(checked((int)w), checked((int)h));
            image.ReadRegion(level, x, y, w, h, ref buffer.GetPinnableReference());
            return buffer;
        }

        public static Bgra32ImageBuffer ReadAssociatedImageBuffer(this OpenSlideImage image, string name, long w, long h)
        {
            var buffer = new Bgra32ImageBuffer(checked((int)w), checked((int)h));
            image.ReadAssociatedImage(name, ref buffer.GetPinnableReference());
            return buffer;
        }
    }
}
