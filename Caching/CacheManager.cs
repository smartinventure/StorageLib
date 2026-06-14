using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace StorageLib.Caching
{
    /// <summary>
    /// Default implementation of the cache manager.
    /// </summary>
    public class CacheManager : ICacheManager
    {
        private readonly ConcurrentDictionary<string, ICache> _caches = new(StringComparer.OrdinalIgnoreCase);
        private readonly Microsoft.Extensions.Caching.Memory.MemoryCache _byteCache;
        private readonly ConcurrentDictionary<string, byte[]> _keyRegistry = new(StringComparer.OrdinalIgnoreCase);
        
        // Dictionary mapping layer names to their byte caches
        private readonly ConcurrentDictionary<string, Microsoft.Extensions.Caching.Memory.MemoryCache> _layerByteCaches = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, byte[]>> _layerKeyRegistries = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheManager"/> class.
        /// </summary>
        public CacheManager()
        {
            _byteCache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 1024 * 1024 * 100 // 100 MB limit
            });
        }

        /// <summary>
        /// Gets or creates a cache with the specified name.
        /// </summary>
        /// <param name="cacheName">The name of the cache.</param>
        /// <returns>The cache instance.</returns>
        public ICache GetOrCreateCache(string cacheName)
        {
            return _caches.GetOrAdd(cacheName, name => new MemoryBasedCache(name));
        }

        /// <summary>
        /// Gets a cache with the specified name if it exists.
        /// </summary>
        /// <param name="cacheName">The name of the cache.</param>
        /// <returns>The cache instance, or null if no cache with that name exists.</returns>
        public ICache? GetCache(string cacheName)
        {
            _caches.TryGetValue(cacheName, out var cache);
            return cache;
        }

        /// <summary>
        /// Removes a cache with the specified name.
        /// </summary>
        /// <param name="cacheName">The name of the cache.</param>
        /// <returns>True if the cache was removed, false if it did not exist.</returns>
        public bool RemoveCache(string cacheName)
        {
            if (_caches.TryRemove(cacheName, out var cache))
            {
                (cache as IDisposable)?.Dispose();
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets all cache names registered with this manager.
        /// </summary>
        /// <returns>An enumerable of cache names.</returns>
        public IEnumerable<string> GetCacheNames()
        {
            return _caches.Keys;
        }

        /// <summary>
        /// Clears all caches registered with this manager.
        /// </summary>
        public void ClearAllCaches()
        {
            foreach (var cache in _caches.Values)
            {
                cache.Clear();
            }

            // Clear the global byte cache
            _byteCache.Dispose();
            _keyRegistry.Clear();
            
            // Clear all layer-specific byte caches
            foreach (var layerCache in _layerByteCaches.Values)
            {
                layerCache.Dispose();
            }
            _layerByteCaches.Clear();
            _layerKeyRegistries.Clear();
        }

        /// <summary>
        /// Gets bytes from the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <returns>The cached bytes, or null if not found.</returns>
        [Obsolete("Use GetLayerBytesAsync instead which provides layer isolation")]
        public Task<byte[]> GetBytesAsync(string key)
        {
            if (_byteCache.TryGetValue(key, out byte[] value))
            {
                return Task.FromResult(value);
            }
            return Task.FromResult<byte[]>(null);
        }

        /// <summary>
        /// Sets bytes in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The bytes to cache.</param>
        /// <param name="expiration">The expiration time span.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        [Obsolete("Use SetLayerBytesAsync instead which provides layer isolation")]
        public Task SetBytesAsync(string key, byte[] value, TimeSpan expiration)
        {
            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(expiration)
                .SetSize(value.Length);

            _byteCache.Set(key, value, cacheEntryOptions);
            _keyRegistry[key] = null; // Just store the key for pattern matching

            return Task.CompletedTask;
        }
        
        /// <summary>
        /// Gets bytes from the cache for a specific layer.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <param name="key">The cache key.</param>
        /// <returns>The cached bytes, or null if not found.</returns>
        public Task<byte[]> GetLayerBytesAsync(string layerName, string key)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return GetBytesAsync(key); // Fall back to global cache if no layer specified
            }
            
            // Get or create the layer's byte cache
            var layerCache = GetOrCreateLayerByteCache(layerName);
            
            if (layerCache.TryGetValue(key, out byte[] value))
            {
                return Task.FromResult(value);
            }
            return Task.FromResult<byte[]>(null);
        }
        
        /// <summary>
        /// Sets bytes in the cache for a specific layer.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The bytes to cache.</param>
        /// <param name="expiration">The expiration time span.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public Task SetLayerBytesAsync(string layerName, string key, byte[] value, TimeSpan expiration)
        {
            if (string.IsNullOrEmpty(layerName))
            {
                return SetBytesAsync(key, value, expiration); // Fall back to global cache if no layer specified
            }
            
            // Get or create the layer's byte cache
            var layerCache = GetOrCreateLayerByteCache(layerName);
            
            var cacheEntryOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Size = value.Length
            };

            layerCache.Set(key, value, cacheEntryOptions);
            
            // Add to the layer's key registry for pattern matching
            var layerKeyRegistry = GetOrCreateLayerKeyRegistry(layerName);
            layerKeyRegistry[key] = null; // Just store the key for pattern matching

            return Task.CompletedTask;
        }

        /// <summary>
        /// Clears cache entries that match the specified pattern.
        /// </summary>
        /// <param name="pattern">The pattern to match (e.g., "prefix*").</param>
        public void ClearCacheByPattern(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return;

            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");

            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

            // Find matching keys
            var keysToRemove = _keyRegistry.Keys
                .Where(k => regex.IsMatch(k))
                .ToList();

            // Remove from cache
            foreach (var key in keysToRemove)
            {
                _byteCache.Remove(key);
                _keyRegistry.TryRemove(key, out _);
            }
        }
        
        /// <summary>
        /// Clears cache entries for a specific layer that match the specified pattern.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <param name="pattern">The pattern to match (e.g., "prefix*").</param>
        public void ClearLayerCacheByPattern(string layerName, string pattern)
        {
            if (string.IsNullOrEmpty(layerName) || string.IsNullOrEmpty(pattern))
                return;
                
            if (!_layerKeyRegistries.TryGetValue(layerName, out var layerKeyRegistry) || 
                !_layerByteCaches.TryGetValue(layerName, out var layerCache))
            {
                return; // No cache found for this layer
            }

            string regexPattern = "^" + Regex.Escape(pattern)
                .Replace("\\*", ".*")
                .Replace("\\?", ".");

            var regex = new Regex(regexPattern, RegexOptions.IgnoreCase);

            // Find matching keys for this layer
            var keysToRemove = layerKeyRegistry.Keys
                .Where(k => regex.IsMatch(k))
                .ToList();

            // Remove from the layer's cache
            foreach (var key in keysToRemove)
            {
                layerCache.Remove(key);
                layerKeyRegistry.TryRemove(key, out _);
            }
        }
        
        /// <summary>
        /// Gets or creates a byte cache for a specific layer.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <returns>The memory cache for the layer.</returns>
        private Microsoft.Extensions.Caching.Memory.MemoryCache GetOrCreateLayerByteCache(string layerName)
        {
            return _layerByteCaches.GetOrAdd(layerName, _ => new Microsoft.Extensions.Caching.Memory.MemoryCache(new MemoryCacheOptions
            {
                SizeLimit = 1024 * 1024 * 100 // 100 MB limit per layer
            }));
        }
        
        /// <summary>
        /// Gets or creates a key registry for a specific layer.
        /// </summary>
        /// <param name="layerName">The name of the layer.</param>
        /// <returns>The key registry for the layer.</returns>
        private ConcurrentDictionary<string, byte[]> GetOrCreateLayerKeyRegistry(string layerName)
        {
            return _layerKeyRegistries.GetOrAdd(layerName, _ => new ConcurrentDictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));
        }
    }
}