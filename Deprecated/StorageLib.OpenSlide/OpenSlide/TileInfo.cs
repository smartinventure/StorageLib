namespace StorageLib.OpenSlide
{
    /// <summary>
    /// Information of a tile.
    /// </summary>
    public class TileInfo
    {
        /// <summary>
        /// The X coordinate in the base level.
        /// </summary>
        public long X { get; set; }
        //TEST

        /// <summary>
        /// The Y coordinate in the base level.
        /// </summary>
        public long Y { get; set; }

        /// <summary>
        /// The corresponding level in the OpenSlide image.
        /// </summary>
        public int SlideLevel { get; set; }

        /// <summary>
        /// Width of the image to read from OpenSlide image.
        /// </summary>
        public long Width { get; set; }

        /// <summary>
        /// Height of the image to read from OpenSlide image.
        /// </summary>
        public long Height { get; set; }

        /// <summary>
        /// The width of the deep zoom tile.
        /// </summary>
        public int TileWidth { get; set; }

        /// <summary>
        /// The height of the deep zoom tile.
        /// </summary>
        public int TileHeight { get; set; }
    }
}