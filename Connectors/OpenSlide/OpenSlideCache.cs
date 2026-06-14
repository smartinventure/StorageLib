using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using StorageLib.Abstractions;
using StorageLib.Caching;

namespace StorageLib.Connectors.OpenSlide
{
    /// <summary>
    /// Cache for OpenSlide slides and tiles.
    /// </summary>
    public class OpenSlideCache
    {
        private readonly ICacheManager _cacheManager;
        private readonly ConcurrentDictionary<string, SlideInfo> _slides;
        private readonly ConcurrentDictionary<string, long> _tileSizes;
        private readonly string _layerName;

        // Cache keys
        private const string SLIDE_KEY_PREFIX = "slide:";
        private const string TILE_KEY_PREFIX = "tile:";
        private const string TILE_SIZE_KEY_PREFIX = "tilesize:";

        /// <summary>
        /// Initializes a new instance of the <see cref="OpenSlideCache"/> class.
        /// </summary>
        /// <param name="cacheManager">The cache manager to use.</param>
        /// <param name="layerName">Optional layer name for cache isolation. Defaults to "openslide".</param>
        public OpenSlideCache(ICacheManager cacheManager, string layerName = "openslide")
        {
            _cacheManager = cacheManager ?? throw new ArgumentNullException(nameof(cacheManager));
            _slides = new ConcurrentDictionary<string, SlideInfo>(StringComparer.OrdinalIgnoreCase);
            _tileSizes = new ConcurrentDictionary<string, long>();
            _layerName = string.IsNullOrEmpty(layerName) ? "openslide" : layerName;
        }

        /// <summary>
        /// Adds or updates a slide in the cache.
        /// </summary>
        /// <param name="slideInfo">The slide information to cache.</param>
        public void AddOrUpdateSlide(SlideInfo slideInfo)
        {
            if (slideInfo == null) return;

            string key = NormalizeVirtualPath(slideInfo.VirtualPath);
            _slides[key] = slideInfo;
        }

        /// <summary>
        /// Removes a slide from the cache.
        /// </summary>
        /// <param name="virtualPath">The virtual path of the slide to remove.</param>
        /// <returns>True if the slide was removed, false otherwise.</returns>
        public bool RemoveSlide(string virtualPath)
        {
            string key = NormalizeVirtualPath(virtualPath);
            return _slides.TryRemove(key, out _);
        }

        /// <summary>
        /// Gets a slide by its virtual path.
        /// </summary>
        /// <param name="virtualPath">The virtual path of the slide.</param>
        /// <returns>The slide information, or null if not found.</returns>
        public SlideInfo GetSlideByVirtualPath(string virtualPath)
        {
            string key = NormalizeVirtualPath(virtualPath);
            _slides.TryGetValue(key, out var slideInfo);
            return slideInfo;
        }

        /// <summary>
        /// Gets all registered slides.
        /// </summary>
        /// <returns>A collection of all registered slides.</returns>
        public IEnumerable<SlideInfo> GetAllSlides()
        {
            return _slides.Values;
        }

        /// <summary>
        /// Gets a tile from the cache or adds it if it doesn't exist.
        /// </summary>
        /// <param name="slideInfo">The slide information.</param>
        /// <param name="level">The level of the tile.</param>
        /// <param name="column">The column of the tile.</param>
        /// <param name="row">The row of the tile.</param>
        /// <param name="factory">The factory function to create the tile if it doesn't exist.</param>
        /// <returns>The tile data.</returns>
        public async Task<byte[]> GetOrAddTileAsync(SlideInfo slideInfo, int level, int column, int row, Func<Task<byte[]>> factory)
        {
            if (slideInfo == null) return Array.Empty<byte>();

            string cacheKey = GetTileCacheKey(slideInfo.VirtualPath, level, column, row);

            // Try to get from layer-specific cache first
            var cachedTile = await _cacheManager.GetLayerBytesAsync(_layerName, cacheKey);
            if (cachedTile != null && cachedTile.Length > 0)
            {
                return cachedTile;
            }

            // Generate the tile
            var tileData = await factory();

            // Cache it in layer-specific cache
            if (tileData != null && tileData.Length > 0)
            {
                await _cacheManager.SetLayerBytesAsync(_layerName, cacheKey, tileData, TimeSpan.FromHours(24));

                // Store the tile size for estimations
                GetOrAddTileSize(slideInfo, level, column, row, tileData.Length);
            }

            return tileData;
        }

        /// <summary>
        /// Tries to get the size of a tile from the cache.
        /// </summary>
        /// <param name="slideInfo">The slide information.</param>
        /// <param name="level">The level of the tile.</param>
        /// <param name="column">The column of the tile.</param>
        /// <param name="row">The row of the tile.</param>
        /// <param name="size">The size of the tile if found.</param>
        /// <returns>True if the size was found in the cache, false otherwise.</returns>
        public bool TryGetTileSize(SlideInfo slideInfo, int level, int column, int row, out long size)
        {
            size = 0;
            if (slideInfo == null) return false;

            string sizeKey = GetTileSizeKey(slideInfo.VirtualPath, level, column, row);
            return _tileSizes.TryGetValue(sizeKey, out size);
        }

        /// <summary>
        /// Gets or adds the size of a tile to the cache.
        /// </summary>
        /// <param name="slideInfo">The slide information.</param>
        /// <param name="level">The level of the tile.</param>
        /// <param name="column">The column of the tile.</param>
        /// <param name="row">The row of the tile.</param>
        /// <param name="size">The size of the tile.</param>
        /// <returns>The size of the tile.</returns>
        public long GetOrAddTileSize(SlideInfo slideInfo, int level, int column, int row, long size)
        {
            if (slideInfo == null) return 0;

            string sizeKey = GetTileSizeKey(slideInfo.VirtualPath, level, column, row);
            return _tileSizes.GetOrAdd(sizeKey, size);
        }

        /// <summary>
        /// Clears the tile cache for a specific slide.
        /// </summary>
        /// <param name="slideInfo">The slide information.</param>
        public void ClearTileCache(SlideInfo slideInfo)
        {
            if (slideInfo == null) return;

            // Clear tile size cache
            string prefix = GetTileSizeKeyPrefix(slideInfo.VirtualPath);
            var keysToRemove = new List<string>();

            foreach (var key in _tileSizes.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                _tileSizes.TryRemove(key, out _);
            }

            // Clear tile data cache for the specific layer
            prefix = GetTileCacheKeyPrefix(slideInfo.VirtualPath);
            _cacheManager.ClearLayerCacheByPattern(_layerName, prefix + "*");
        }

        #region Helper Methods

        private static string NormalizeVirtualPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return string.Empty;
                
            // Simple path normalization - replace backslashes with forward slashes and trim
            return virtualPath.Replace('\\', '/').Trim('/');
        }

        private string GetTileCacheKey(string slidePath, int level, int column, int row)
        {
            return $"{TILE_KEY_PREFIX}{slidePath}:{level}:{column}:{row}";
        }

        private string GetTileCacheKeyPrefix(string slidePath)
        {
            return $"{TILE_KEY_PREFIX}{slidePath}:";
        }

        private string GetTileSizeKey(string slidePath, int level, int column, int row)
        {
            return $"{TILE_SIZE_KEY_PREFIX}{slidePath}:{level}:{column}:{row}";
        }

        private string GetTileSizeKeyPrefix(string slidePath)
        {
            return $"{TILE_SIZE_KEY_PREFIX}{slidePath}:";
        }

        #endregion
    }
}