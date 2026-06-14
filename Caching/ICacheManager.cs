using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace StorageLib.Caching
{
    /// <summary>
    /// Provides a central registry for named caches in the application.
    /// </summary>
    public interface ICacheManager
    {
        /// <summary>
        /// Gets or creates a cache with the specified name.
        /// </summary>
        /// <param name="cacheName">The name of the cache.</param>
        /// <returns>The cache instance.</returns>
        ICache GetOrCreateCache(string cacheName);

        /// <summary>
        /// Gets a cache with the specified name if it exists.
        /// </summary>
        /// <param name="cacheName">The name of the cache.</param>
        /// <returns>The cache instance, or null if no cache with that name exists.</returns>
        ICache? GetCache(string cacheName);

        /// <summary>
        /// Removes a cache with the specified name.
        /// </summary>
        /// <param name="cacheName">The name of the cache.</param>
        /// <returns>True if the cache was removed, false if it did not exist.</returns>
        bool RemoveCache(string cacheName);

        /// <summary>
        /// Gets all cache names registered with this manager.
        /// </summary>
        /// <returns>An enumerable of cache names.</returns>
        IEnumerable<string> GetCacheNames();

        /// <summary>
        /// Clears all caches registered with this manager.
        /// </summary>
        void ClearAllCaches();

        /// <summary>
        /// Gets bytes from the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <returns>The cached bytes, or null if not found.</returns>
        [Obsolete("Use GetLayerBytesAsync instead which provides layer isolation")]
        Task<byte[]> GetBytesAsync(string key);

        /// <summary>
        /// Sets bytes in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The bytes to cache.</param>
        /// <param name="expiration">The expiration time span.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Obsolete("Use SetLayerBytesAsync instead which provides layer isolation")]
        Task SetBytesAsync(string key, byte[] value, TimeSpan expiration);

        /// <summary>
        /// Gets bytes from the cache for a specific layer.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <param name="key">The cache key.</param>
        /// <returns>The cached bytes, or null if not found.</returns>
        Task<byte[]> GetLayerBytesAsync(string layerName, string key);

        /// <summary>
        /// Sets bytes in the cache for a specific layer.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The bytes to cache.</param>
        /// <param name="expiration">The expiration time span.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        Task SetLayerBytesAsync(string layerName, string key, byte[] value, TimeSpan expiration);

        /// <summary>
        /// Clears cache entries that match the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern to match (e.g., "prefix*").</param>
        void ClearCacheByPattern(string pattern);

        /// <summary>
        /// Clears cache entries for a specific layer that match the specified pattern.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <param name="pattern">The pattern to match (e.g., "prefix*").</param>
        void ClearLayerCacheByPattern(string layerName, string pattern);
    }
}