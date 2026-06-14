namespace StorageLib.OpenSlide
{
    /// <summary>
    /// Represents the image dimensions
    /// </summary>
    public readonly struct ImageDimensions
    {
        private readonly long _width;
        private readonly long _height;
        //test

        /// <summary>
        /// The width of the image.
        /// </summary>
        public long Width => _width;

        /// <summary>
        /// The height of the image.
        /// </summary>
        public long Height => _height;

        /// <summary>
        /// Initialize a new <see cref="ImageDimensions"/> struct.
        /// </summary>
        /// <param name="width">The width of the image.</param>
        /// <param name="height">The height of the image.</param>
        public ImageDimensions(long width, long height)
        {
            _width = width;
            _height = height;
        }

        /// <summary>
        /// Deconstruction the struct.
        /// </summary>
        /// <param name="width">The width of the image.</param>
        /// <param name="height">The height of the image.</param>
        public void Deconstruct(out long width, out long height)
        {
            width = _width;
            height = _height;
        }

        /// <summary>
        /// Converts the <see cref="ImageDimensions"/> struct into a tuple of (Width, Height).
        /// </summary>
        /// <param name="dimensions">the <see cref="ImageDimensions"/> struct.</param>
        public static implicit operator (long Width, long Height)(ImageDimensions dimensions)
        {
            return (Width: dimensions._width, Height: dimensions._height);
        }

        /// <summary>
        /// Converts a tuple of (Width, Height) into the <see cref="ImageDimensions"/> struct.
        /// </summary>
        /// <param name="dimensions">A tuple of (Width, Height).</param>
        public static explicit operator ImageDimensions(ValueTuple<long, long> dimensions)
        {
            return new ImageDimensions(dimensions.Item1, dimensions.Item2);
        }
    }
}