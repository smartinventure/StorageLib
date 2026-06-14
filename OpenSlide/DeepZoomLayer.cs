namespace StorageLib.OpenSlide
{
    internal readonly struct DeepZoomLayer
    {
        public DeepZoomLayer(long width, long height)
        {
            Width = width;
            Height = height;
        }

        public long Width { get; }

        public long Height { get; }
    }
}