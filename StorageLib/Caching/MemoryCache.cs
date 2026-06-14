using Microsoft.Extensions.Caching.Memory;
using StorageLib.Caching;
using System.Collections.Concurrent;

namespace FolderMagicLib.Caching
{
    /// <summary>
    /// Implementation of ICache using Microsoft's IMemoryCache.
    /// </summary>
    public class MemoryCache : ICache, IDisposable
    {
        private readonly IMemoryCache _cache;
        private readonly ConcurrentDictionary<string, object> _keys = new();
        private bool _disposed;

        /// <summary>
        /// Gets the name of this cache.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets the current count of items in the cache.
        /// </summary>
        public int Count => _keys.Count;

        /// <summary>
        /// Initializes a new instance of the MemoryCache class.
        /// </summary>
        /// <param name="name">The name of the cache.</param>
        public MemoryCache(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new MemoryCacheOptions());
        }

        /// <summary>
        /// Gets a value from the cache.
        /// </summary>
        /// <typeparam name="T">The type of the value to get.</typeparam>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>The value, or default(T) if not found.</returns>
        public T Get<T>(string key)
        {
            if (_cache.TryGetValue(key, out object value) && value is T typedValue)
            {
                return typedValue;
            }
            return default;
        }

        /// <summary>
        /// Sets a value in the cache.
        /// </summary>
        /// <typeparam name="T">The type of the value to set.</typeparam>
        /// <param name="key">The key of the value to set.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="absoluteExpirationRelativeToNow">The expiration time relative to now, or null for no expiration.</param>
        public void Set<T>(string key, T value, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            var options = new MemoryCacheEntryOptions();

            if (absoluteExpirationRelativeToNow.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow.Value;
            }

            options.RegisterPostEvictionCallback(OnCacheEntryRemoved);

            _cache.Set(key, value, options);
            _keys[key] = null; // Just track keys for Count property
        }

        /// <summary>
        /// Gets an item from the cache, or computes and adds it if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of the item.</typeparam>
        /// <param name="key">The key to look up.</param>
        /// <param name="factory">A function that produces the value if it's not in the cache.</param>
        /// <param name="absoluteExpirationRelativeToNow">Optional expiration time relative to now.</param>
        /// <returns>The cached or computed value.</returns>
        public T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            if (!_cache.TryGetValue(key, out T value))
            {
                value = factory();
                Set(key, value, absoluteExpirationRelativeToNow);
            }

            return value;
        }

        /// <summary>
        /// Gets an item from the cache, or computes and adds it if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of the item.</typeparam>
        /// <param name="key">The key to look up.</param>
        /// <param name="valueFactory">An asynchronous function that produces the value if it's not in the cache.</param>
        /// <param name="absoluteExpirationRelativeToNow">Optional expiration time relative to now.</param>
        /// <returns>A task representing the cached or computed value.</returns>
        public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> valueFactory, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (valueFactory == null) throw new ArgumentNullException(nameof(valueFactory));

            if (!_cache.TryGetValue(key, out T value))
            {
                value = await valueFactory();
                Set(key, value, absoluteExpirationRelativeToNow);
            }

            return value;
        }

        /// <summary>
        /// Attempts to get an existing item from the cache.
        /// </summary>
        /// <typeparam name="T">The type of the item.</typeparam>
        /// <param name="key">The key to look up.</param>
        /// <param name="value">The output value if found.</param>
        /// <returns>True if the item was found, false otherwise.</returns>
        public bool TryGetValue<T>(string key, out T value)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return _cache.TryGetValue(key, out value);
        }

        /// <summary>
        /// Determines whether the cache contains a value with the specified key.
        /// </summary>
        /// <param name="key">The key to check for.</param>
        /// <returns>True if the cache contains a value with the specified key, false otherwise.</returns>
        public bool Contains(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            return _keys.ContainsKey(key);
        }

        /// <summary>
        /// Removes an item from the cache.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>True if the item was removed, false if it wasn't found.</returns>
        public bool Remove(string key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));

            _cache.Remove(key);
            return _keys.TryRemove(key, out _);
        }

        /// <summary>
        /// Clears all items from the cache.
        /// </summary>
        public void Clear()
        {
            foreach (var key in _keys.Keys.ToArray())
            {
                _cache.Remove(key);
            }
            _keys.Clear();
        }

        private void OnCacheEntryRemoved(object key, object value, EvictionReason reason, object state)
        {
            if (key != null && key is string stringKey)
            {
                _keys.TryRemove(stringKey, out _);
            }
        }

        /// <summary>
        /// Disposes the memory cache.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the memory cache.
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _cache.Dispose();
                }

                _disposed = true;
            }
        }
    }
}