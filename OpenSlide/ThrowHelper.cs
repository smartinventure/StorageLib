using StorageLib.OpenSlide.Interop;

namespace StorageLib.OpenSlide
{
    public static class ThrowHelper
    {
        public static void CheckAndThrowError(OpenSlideImageSafeHandle osr)
        {
            string message = OpenSlideInterop.GetError(osr);
            if (!(message is null))
            {
                ThrowOpenSlideException(message);
            }
        }

        public static bool TryCheckError(OpenSlideImageSafeHandle osr, out string message)
        {
            message = OpenSlideInterop.GetError(osr);
            return message == null;
        }

        private static void ThrowOpenSlideException(string message)
        {
            throw new OpenSlideException(message);
        }
    }
}
