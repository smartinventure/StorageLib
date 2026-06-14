using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace StorageLib.Connectors.OpenSlide
{
    /// <summary>
    /// Represents information about a slide, including metadata and level information.
    /// </summary>
    public class SlideInfo
    {
        /// <summary>
        /// Gets or sets the source path of the slide.
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// Gets or sets the virtual path of the slide.
        /// </summary>
        public string VirtualPath { get; set; }

        /// <summary>
        /// Gets or sets the filename of the slide (without path).
        /// </summary>
        public string SlideFilename { get; set; }

        /// <summary>
        /// Gets or sets the tile size.
        /// </summary>
        public int TileSize { get; set; }

        /// <summary>
        /// Gets or sets the tile format.
        /// </summary>
        public OpenSlideCommand.TileFormat Format { get; set; }

        /// <summary>
        /// Gets or sets the JPEG quality (1-100).
        /// </summary>
        public int JpegQuality { get; set; }

        /// <summary>
        /// Gets or sets the width of the slide in pixels.
        /// </summary>
        public long Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the slide in pixels.
        /// </summary>
        public long Height { get; set; }

        /// <summary>
        /// Gets or sets the number of DeepZoom levels.
        /// </summary>
        public int LevelCount { get; set; }

        /// <summary>
        /// Gets or sets the microns per pixel in the X dimension.
        /// </summary>
        public double? MicronsPerPixelX { get; set; }

        /// <summary>
        /// Gets or sets the microns per pixel in the Y dimension.
        /// </summary>
        public double? MicronsPerPixelY { get; set; }

        /// <summary>
        /// Gets or sets the X coordinate of the bounds.
        /// </summary>
        public long BoundsX { get; set; }

        /// <summary>
        /// Gets or sets the Y coordinate of the bounds.
        /// </summary>
        public long BoundsY { get; set; }

        /// <summary>
        /// Gets or sets the width of the bounds.
        /// </summary>
        public long BoundsWidth { get; set; }

        /// <summary>
        /// Gets or sets the height of the bounds.
        /// </summary>
        public long BoundsHeight { get; set; }

        /// <summary>
        /// Gets or sets the vendor.
        /// </summary>
        public string Vendor { get; set; }

        /// <summary>
        /// Gets or sets the objective power.
        /// </summary>
        public string ObjectivePower { get; set; }

        /// <summary>
        /// Gets or sets the comment.
        /// </summary>
        public string Comment { get; set; }

        /// <summary>
        /// Gets or sets the levels.
        /// </summary>
        public List<LevelInfo> Levels { get; set; } = new List<LevelInfo>();

        /// <summary>
        /// Gets or sets the properties.
        /// </summary>
        public Dictionary<string, string> Properties { get; set; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets the file extension for the selected format.
        /// </summary>
        [JsonIgnore]
        public string FormatExtension
        {
            get
            {
                return Format switch
                {
                    OpenSlideCommand.TileFormat.JPEG => "jpeg",
                    OpenSlideCommand.TileFormat.PNG => "png",
                    OpenSlideCommand.TileFormat.BGRA => "bgra",
                    _ => "jpeg"
                };
            }
        }

        /// <summary>
        /// Gets a level info by level.
        /// </summary>
        /// <param name="level">The level to get info for.</param>
        /// <returns>The level info or null if not found.</returns>
        public LevelInfo GetLevelInfo(int level)
        {
            return Levels.Find(l => l.Level == level);
        }
    }

    /// <summary>
    /// Represents information about a level in a DeepZoom pyramid.
    /// </summary>
    public class LevelInfo
    {
        /// <summary>
        /// Gets or sets the level index (0 is the most zoomed out level).
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// Gets or sets the width of the level in pixels.
        /// </summary>
        public int Width { get; set; }

        /// <summary>
        /// Gets or sets the height of the level in pixels.
        /// </summary>
        public int Height { get; set; }

        /// <summary>
        /// Gets or sets the downsample factor for this level.
        /// </summary>
        public double Downsample { get; set; }

        /// <summary>
        /// Gets or sets the number of tile columns in this level.
        /// </summary>
        public int Columns { get; set; }

        /// <summary>
        /// Gets or sets the number of tile rows in this level.
        /// </summary>
        public int Rows { get; set; }

        /// <summary>
        /// Gets or sets the OpenSlide level that is the closest match for this DeepZoom level.
        /// </summary>
        public int OpenSlideLevel { get; set; }
    }
}