using System;

namespace StorageLib.Connectors.OpenSlide
{
    /// <summary>
    /// Contains information about a specific tile in the DeepZoom pyramid.
    /// </summary>
    public class TileInfo
    {
        /// <summary>
        /// The DeepZoom level of the tile (0 = smallest, LevelCount-1 = largest).
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// The column index of the tile.
        /// </summary>
        public int Column { get; set; }

        /// <summary>
        /// The row index of the tile.
        /// </summary>
        public int Row { get; set; }

        /// <summary>
        /// The x-coordinate in the slide coordinate system (level 0).
        /// </summary>
        public long X { get; set; }

        /// <summary>
        /// The y-coordinate in the slide coordinate system (level 0).
        /// </summary>
        public long Y { get; set; }

        /// <summary>
        /// The width of the tile in the slide coordinate system.
        /// </summary>
        public long Width { get; set; }

        /// <summary>
        /// The height of the tile in the slide coordinate system.
        /// </summary>
        public long Height { get; set; }

        /// <summary>
        /// The width of the tile in pixels (may be smaller at edges).
        /// </summary>
        public int TileWidth { get; set; }

        /// <summary>
        /// The height of the tile in pixels (may be smaller at edges).
        /// </summary>
        public int TileHeight { get; set; }

        /// <summary>
        /// The best OpenSlide level to use for this tile.
        /// </summary>
        public int OpenSlideLevel { get; set; }

        /// <summary>
        /// The virtual path of the tile in the file system.
        /// </summary>
        public string VirtualPath { get; set; }

        /// <summary>
        /// Gets or sets the format of the tile.
        /// </summary>
        public OpenSlideCommand.TileFormat Format { get; set; }

        /// <summary>
        /// Gets or sets the JPEG quality.
        /// </summary>
        public int JpegQuality { get; set; }

        /// <summary>
        /// Estimated file size in bytes (for GetAttr).
        /// </summary>
        public long EstimatedSize { get; set; }

        /// <summary>
        /// Actual file size in bytes (if known).
        /// </summary>
        public long ActualSize { get; set; }

        /// <summary>
        /// Calculates the estimated size of the tile.
        /// </summary>
        /// <returns>The estimated size in bytes.</returns>
        public long CalculateEstimatedSize()
        {
            // Delegate to the static DeepZoomHandler method that handles size estimation
            int width = Math.Min(TileWidth, (int)Width);
            int height = Math.Min(TileHeight, (int)Height);

            return DeepZoomHandler.EstimateTileSize(Format, width, height, JpegQuality);
        }
    }
}