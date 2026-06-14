namespace StorageLib.OpenSlide
{
    /// <summary>
    /// Deep zoom generator for OpenSlide image.
    /// </summary>
    public class DeepZoomGenerator
    {
        private readonly OpenSlideImage _image;
        private readonly long _boundX;
        private readonly long _boundY;
        private readonly int _tileSize;
        private readonly int _overlap;
        private readonly DeepZoomLayerInformation[] _layers;

        private IEnumerable<(int, int)> _levelTilesCache;
        private IEnumerable<(long, long)> _levelDimensionsCache;
        private int? _tileCountCache;

        /// <summary>
        /// Initializes a new instance of the <see cref="DeepZoomGenerator"/> class.
        /// </summary>
        /// <param name="image">The OpenSlide image.</param>
        /// <param name="tileSize">The tile size param.</param>
        /// <param name="overlap">The overlap param.</param>
        /// <param name="limitBounds">Whether image bounds should be respected.</param>
        public DeepZoomGenerator(OpenSlideImage image, int tileSize = 256, int overlap = 1, bool limitBounds = true)
        {
            _image = image ?? throw new ArgumentNullException(nameof(image));

            if (tileSize <= 0)
            {
                throw new ArgumentException("tileSize should be more than 0", nameof(tileSize));
            }


            long width, height;
            if (limitBounds)
            {
                _boundX = image.BoundsX ?? 0;
                _boundY = image.BoundsY ?? 0;
                width = image.BoundsWidth ?? image.Width;
                height = image.BoundsHeight ?? image.Height;
            }
            else
            {
                _boundX = 0;
                _boundY = 0;
                width = image.Width;
                height = image.Height;
            }

            DeepZoomLayer[] dzLayers = DeepZoomHelper.CalculateDeepZoomLayers(width, height);
            DeepZoomLayerInformation[] layers = new DeepZoomLayerInformation[dzLayers.Length];

            for (int i = 0; i < layers.Length; i++)
            {
                DeepZoomLayer dzLayer = dzLayers[i];
                int layerDownsample = 1 << (dzLayers.Length - i - 1);

                int level = image.GetBestLevelForDownsample(layerDownsample);
                (long levelWidth, long levelHeight) = image.GetLevelDimensions(level);
                layers[i] = new DeepZoomLayerInformation
                {
                    Level = level,
                    LevelDownsample = image.GetLevelDownsample(level),
                    LevelWidth = levelWidth,
                    LevelHeight = levelHeight,
                    LayerDownsample = layerDownsample,
                    LayerWidth = dzLayer.Width,
                    LayerHeight = dzLayer.Height
                };
            }

            _layers = layers;
            _tileSize = tileSize;
            _overlap = overlap;
        }

        /// <summary>
        /// The Count of the deep zoom level.
        /// </summary>
        public int LevelCount => _layers.Length;

        /// <summary>
        /// <see cref="OpenSlideImage"/>
        /// </summary>
        public OpenSlideImage Image => _image;

        /// <summary>
        /// The number of tiles in each level.
        /// </summary>
        public IEnumerable<(int HorizontalTileCount, int VerticalTileCount)> LevelTiles
            => _levelTilesCache ??= _layers.Select(l => ((int)((l.LayerWidth + _tileSize - 1) / _tileSize), (int)((l.LayerHeight + _tileSize - 1) / _tileSize)));

        /// <summary>
        /// The dimensions of each level.
        /// </summary>
        public IEnumerable<(long Width, long Height)> LevelDimensions => _levelDimensionsCache ??= _layers.Select(l => (l.LayerWidth, l.LayerHeight));

        /// <summary>
        /// The total number of tiles.
        /// </summary>
        public int TileCount => _tileCountCache ?? (_tileCountCache = (int)_layers.Sum(l => ((l.LayerWidth + _tileSize - 1) / _tileSize) * ((l.LayerHeight + _tileSize - 1) / _tileSize))).GetValueOrDefault();

        /// <summary>
        /// Get the pre-multiplied BGRA data for the specified tile.
        /// </summary>
        /// <param name="level">The deep zoom level.</param>
        /// <param name="locationX">Horizontal tile index.</param>
        /// <param name="locationY">Vertical tile index.</param>
        /// <param name="info">Information of the specified tile.</param>
        /// <returns>Pre-multiplied BGRA image data.</returns>
        public byte[] GetTile(int level, int locationX, int locationY, out TileInfo info)
        {
            info = GetTileInfo(level, locationX, locationY);
            return _image.ReadRegion(info.SlideLevel, info.X, info.Y, info.Width, info.Height);
        }

        /// <summary>
        /// Get information of the specified tile.
        /// </summary>
        /// <param name="level">The deep zoom level.</param>
        /// <param name="locationX">Horizontal tile index.</param>
        /// <param name="locationY">Vertical tile index.</param>
        /// <returns>Information of the specified tile.</returns>
        public TileInfo GetTileInfo(int level, int locationX, int locationY)
        {
            var layers = _layers;
            if ((uint)level >= (uint)layers.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }

            var layer = layers[level];
            int tileSize = _tileSize;
            int overlap = _overlap;
            int horizontalTileCount = (int)((layer.LayerWidth + tileSize - 1) / tileSize);
            int verticalTileCount = (int)((layer.LayerHeight + tileSize - 1) / tileSize);

            if ((uint)locationX >= (uint)horizontalTileCount)
            {
                throw new ArgumentOutOfRangeException(nameof(locationX));
            }

            if ((uint)locationY >= (uint)verticalTileCount)
            {
                throw new ArgumentOutOfRangeException(nameof(locationY));
            }

            long offsetX = (long)locationX * tileSize;
            long offsetY = (long)locationY * tileSize;
            int width = (int)Math.Min(tileSize, layer.LayerWidth - offsetX);
            int height = (int)Math.Min(tileSize, layer.LayerHeight - offsetY);

            if (locationX != 0)
            {
                offsetX -= overlap;
                width += overlap;
            }

            if (locationX != horizontalTileCount - 1)
            {
                width += overlap;
            }

            if (locationY != 0)
            {
                offsetY -= overlap;
                height += overlap;
            }

            if (locationY != verticalTileCount - 1)
            {
                height += overlap;
            }

            return new TileInfo
            {
                X = _boundX + (offsetX * layer.LayerDownsample),
                Y = _boundY + (offsetY * layer.LayerDownsample),
                SlideLevel = layer.Level,
                Width = (long)(width * layer.LayerDownsample / layer.LevelDownsample),
                Height = (long)(height * layer.LayerDownsample / layer.LevelDownsample),
                TileWidth = width,
                TileHeight = height
            };
        }
    }
}
