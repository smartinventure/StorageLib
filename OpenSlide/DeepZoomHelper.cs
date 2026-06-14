namespace StorageLib.OpenSlide
{
    internal static class DeepZoomHelper
    {
        internal static DeepZoomLayer[] CalculateDeepZoomLayers(long width, long height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var layers = new List<DeepZoomLayer>();
            var currentLayer = new DeepZoomLayer(width, height);
            layers.Add(currentLayer);

            while (currentLayer.Width != 1 || currentLayer.Height != 1)
            {
                currentLayer = new DeepZoomLayer((currentLayer.Width + 1) / 2, (currentLayer.Height + 1) / 2);
                layers.Add(currentLayer);
            }

            layers.Reverse();
            return layers.ToArray();
        }
    }
}
