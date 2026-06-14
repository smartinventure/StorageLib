using System.Runtime.InteropServices;
using System.Text;

namespace StorageLib.OpenSlide.Interop
{
    /// <summary>
    /// The interop helpler for OpenSlide.
    /// </summary>
    internal static partial class OpenSlideInterop
    {
        private const string LibOpenSlide = "libopenslide-1.dll";

        [DllImport(LibOpenSlide, EntryPoint = "openslide_get_version", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr GetVersionInternal();

        /// <summary>
        /// The name of the property containing a slide's comment, if any. 
        /// </summary>
        public const string OpenSlidePropertyNameComment = "openslide.comment";

        /// <summary>
        /// The name of the property containing a slide's comment, if any. 
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameComment => GetZeroPaddedBytes(OpenSlidePropertyNameComment);

        /// <summary>
        /// The name of the property containing an identification of the vendor. 
        /// </summary>
        public const string OpenSlidePropertyNameVendor = "openslide.vendor";

        /// <summary>
        /// The name of the property containing an identification of the vendor. 
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameVendor => GetZeroPaddedBytes(OpenSlidePropertyNameVendor);

        /// <summary>
        /// The name of the property containing the "quickhash-1" sum. 
        /// </summary>
        public const string OpenSlidePropertyNameQuickHash1 = "openslide.quickhash-1";

        /// <summary>
        /// The name of the property containing the "quickhash-1" sum. 
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameQuickHash1 => GetZeroPaddedBytes(OpenSlidePropertyNameQuickHash1);

        /// <summary>
        /// The name of the property containing a slide's background color, if any.
        /// </summary>
        public const string OpenSlidePropertyNameBackgroundColor = "openslide.background-color";

        /// <summary>
        /// The name of the property containing a slide's background color, if any.
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameBackgroundColor => GetZeroPaddedBytes(OpenSlidePropertyNameBackgroundColor);

        /// <summary>
        /// The name of the property containing a slide's objective power, if known. 
        /// </summary>
        public const string OpenSlidePropertyNameObjectivePower = "openslide.objective-power";

        /// <summary>
        /// The name of the property containing a slide's objective power, if known. 
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameObjectivePower => GetZeroPaddedBytes(OpenSlidePropertyNameObjectivePower);

        /// <summary>
        /// The name of the property containing the number of microns per pixel in the X dimension of level 0, if known.
        /// </summary>
        public const string OpenSlidePropertyNameMPPX = "openslide.mpp-x";

        /// <summary>
        /// The name of the property containing the number of microns per pixel in the X dimension of level 0, if known.
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameMPPX => GetZeroPaddedBytes(OpenSlidePropertyNameMPPX);

        /// <summary>
        /// The name of the property containing the number of microns per pixel in the Y dimension of level 0, if known.
        /// </summary>
        public const string OpenSlidePropertyNameMPPY = "openslide.mpp-y";

        /// <summary>
        /// The name of the property containing the number of microns per pixel in the Y dimension of level 0, if known.
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameMPPY => GetZeroPaddedBytes(OpenSlidePropertyNameMPPY);

        /// <summary>
        /// The name of the property containing the X coordinate of the rectangle bounding the non-empty region of the slide, if available. 
        /// </summary>
        public const string OpenSlidePropertyNameBoundsX = "openslide.bounds-x";

        /// <summary>
        /// The name of the property containing the X coordinate of the rectangle bounding the non-empty region of the slide, if available. 
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameBoundsX => GetZeroPaddedBytes(OpenSlidePropertyNameBoundsX);

        /// <summary>
        /// The name of the property containing the Y coordinate of the rectangle bounding the non-empty region of the slide, if available. 
        /// </summary>
        public const string OpenSlidePropertyNameBoundsY = "openslide.bounds-y";

        /// <summary>
        /// The name of the property containing the Y coordinate of the rectangle bounding the non-empty region of the slide, if available. 
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameBoundsY => GetZeroPaddedBytes(OpenSlidePropertyNameBoundsY);

        /// <summary>
        /// The name of the property containing the width of the rectangle bounding the non-empty region of the slide, if available. 
        /// </summary>
        public const string OpenSlidePropertyNameBoundsWidth = "openslide.bounds-width";

        /// <summary>
        /// The name of the property containing the width of the rectangle bounding the non-empty region of the slide, if available. 
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameBoundsWidth => GetZeroPaddedBytes(OpenSlidePropertyNameBoundsWidth);

        /// <summary>
        /// The name of the property containing the height of the rectangle bounding the non-empty region of the slide, if available.
        /// </summary>
        public const string OpenSlidePropertyNameBoundsHeight = "openslide.bounds-height";

        /// <summary>
        /// The name of the property containing the height of the rectangle bounding the non-empty region of the slide, if available.
        /// </summary>
        public static ReadOnlySpan<byte> Utf8OpenSlidePropertyNameBoundsHeight => GetZeroPaddedBytes(OpenSlidePropertyNameBoundsHeight);

        /// <summary>
        /// Get the version of the OpenSlide library.
        /// </summary>
        public static string GetVersion()
        {
            IntPtr pResult = GetVersionInternal();
            return StringFromNativeUtf8(pResult)!;
        }

        private static byte[] GetZeroPaddedBytes(string value)
        {
            var bytes = new byte[value.Length + 1];
            Encoding.UTF8.GetBytes(value, bytes);
            bytes[bytes.Length - 1] = 0;

            return bytes;
        }
    }
}
