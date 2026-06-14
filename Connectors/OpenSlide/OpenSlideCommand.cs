using System;
using System.Text.Json.Serialization;

namespace StorageLib.Connectors.OpenSlide
{
    /// <summary>
    /// Represents a command to expose a slide file as a DeepZoom pyramid in a virtual directory.
    /// </summary>
    public class OpenSlideCommand
    {
        /// <summary>
        /// Defines the operation to perform on a slide.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum OpenSlideOperation
        {
            /// <summary>
            /// Create a new slide mapping.
            /// </summary>
            Create,

            /// <summary>
            /// Update an existing slide mapping.
            /// </summary>
            Update,

            /// <summary>
            /// Delete an existing slide mapping.
            /// </summary>
            Delete
        }

        /// <summary>
        /// Defines the image format for tiles.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public enum TileFormat
        {
            /// <summary>
            /// Raw uncompressed BGRA format.
            /// </summary>
            BGRA,

            /// <summary>
            /// JPEG compressed format.
            /// </summary>
            JPEG,

            /// <summary>
            /// PNG compressed format.
            /// </summary>
            PNG
        }

        /// <summary>
        /// The path to the physical slide file on disk.
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// The virtual path where the DeepZoom pyramid will be exposed.
        /// </summary>
        public string VirtualPath { get; set; }

        /// <summary>
        /// The operation to perform.
        /// </summary>
        public OpenSlideOperation Operation { get; set; } = OpenSlideOperation.Create;

        /// <summary>
        /// The size of each tile in pixels (both width and height).
        /// </summary>
        public int TileSize { get; set; } = 256;

        /// <summary>
        /// The format for the generated tiles.
        /// </summary>
        public TileFormat Format { get; set; } = TileFormat.BGRA;

        /// <summary>
        /// The quality setting for JPEG compression (0-100, only used for JPEG format).
        /// </summary>
        public int JpegQuality { get; set; } = 85;

        /// <summary>
        /// Gets a value indicating whether the command is valid.
        /// </summary>
        [JsonIgnore]
        public bool IsValid => Validate();

        /// <summary>
        /// Validates the command.
        /// </summary>
        /// <returns>True if the command is valid, false otherwise.</returns>
        public bool Validate()
        {
            // SourcePath is required for Create and Update
            if (Operation == OpenSlideOperation.Create || Operation == OpenSlideOperation.Update)
            {
                if (string.IsNullOrWhiteSpace(SourcePath))
                {
                    return false;
                }
            }

            // VirtualPath is always required
            if (string.IsNullOrWhiteSpace(VirtualPath))
            {
                return false;
            }

            // TileSize should be reasonable
            if (TileSize < 16 || TileSize > 4096)
            {
                return false;
            }

            // JpegQuality should be between 1 and 100
            if (Format == TileFormat.JPEG && (JpegQuality < 1 || JpegQuality > 100))
            {
                return false;
            }

            return true;
        }
    }
}