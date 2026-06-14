using StorageLib.Abstractions;

namespace StorageLib.Streaming
{
    /// <summary>
    /// Extension methods for easy conversion between IStorage and IStreamStorage interfaces
    /// </summary>
    public static class StorageExtensions
    {
        /// <summary>
        /// Converts any IStorage implementation to IStreamStorage using the universal adapter.
        /// This provides .NET-idiomatic Stream-based access while maintaining the existing
        /// IntPtr-based IStorage as the single source of truth.
        /// </summary>
        /// <param name="storage">The IStorage implementation to adapt</param>
        /// <returns>An IStreamStorage adapter that wraps the provided IStorage</returns>
        /// <example>
        /// <code>
        /// // Create any storage (LocalStorage, AzureStorage, etc.)
        /// IStorage storage = new LocalStorage();
        /// await storage.InitializeAsync("", "/path/to/data");
        /// 
        /// // Convert to stream-based interface
        /// IStreamStorage streamStorage = storage.AsStreamStorage();
        /// 
        /// // Use convenient stream operations
        /// using var stream = await streamStorage.OpenReadAsync("file.txt");
        /// string content = await streamStorage.ReadAllTextAsync("file.txt");
        /// await streamStorage.WriteAllBytesAsync("output.bin", data);
        /// </code>
        /// </example>
        public static IStreamStorage AsStreamStorage(this IStorage storage)
        {
            if (storage == null)
                throw new ArgumentNullException(nameof(storage));
            
            return new StreamStorageAdapter(storage);
        }
        
        /// <summary>
        /// Gets the underlying IStorage from an IStreamStorage adapter.
        /// Useful when you need to access the raw IntPtr-based operations for performance.
        /// </summary>
        /// <param name="streamStorage">The IStreamStorage to get the underlying storage from</param>
        /// <returns>The underlying IStorage implementation</returns>
        /// <example>
        /// <code>
        /// IStreamStorage streamStorage = storage.AsStreamStorage();
        /// 
        /// // Later, if you need raw performance
        /// IStorage rawStorage = streamStorage.GetUnderlyingStorage();
        /// IntPtr handle = await rawStorage.OpenAsync("file.bin", OpenFlags.ReadOnly);
        /// // ... direct IntPtr operations
        /// </code>
        /// </example>
        public static IStorage GetUnderlyingStorage(this IStreamStorage streamStorage)
        {
            if (streamStorage == null)
                throw new ArgumentNullException(nameof(streamStorage));
            
            return streamStorage.UnderlyingStorage;
        }
        
        /// <summary>
        /// Checks if the IStreamStorage is actually a StreamStorageAdapter wrapper.
        /// This can be useful for optimization scenarios where you want to avoid
        /// double-wrapping or need to access adapter-specific functionality.
        /// </summary>
        /// <param name="streamStorage">The IStreamStorage to check</param>
        /// <returns>True if this is a StreamStorageAdapter, false otherwise</returns>
        public static bool IsAdapter(this IStreamStorage streamStorage)
        {
            if (streamStorage == null)
                throw new ArgumentNullException(nameof(streamStorage));
            
            return streamStorage is StreamStorageAdapter;
        }
    }
} 