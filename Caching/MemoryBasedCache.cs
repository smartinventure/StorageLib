using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;

namespace StorageLib.Caching
{
    /// <summary>
    /// Memory-based implementation of ICache that uses Microsoft.Extensions.Caching.Memory.
    /// </summary>
    public class MemoryBasedCache : ICache, IDisposable
    {
        private IMemoryCache _cache;
        private readonly string _name;
        private bool _disposed;

        /// <summary>
        /// Initializes a new instance of the <see cref="MemoryBasedCache"/> class.
        /// </summary>
        /// <param name="name">The name of the cache.</param>
        public MemoryBasedCache(string name)
        {
            _name = name ?? throw new ArgumentNullException(nameof(name));
            _cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new MemoryCacheOptions());
        }

        /// <summary>
        /// Gets the name of the cache.
        /// </summary>
        public string Name => _name;

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
            var options = new MemoryCacheEntryOptions();

            if (absoluteExpirationRelativeToNow.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow.Value;
            }

            _cache.Set(key, value, options);
        }

        /// <summary>
        /// Gets a value from the cache or adds it if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of the value to get or add.</typeparam>
        /// <param name="key">The key of the value to get or add.</param>
        /// <param name="factory">A function that produces the value if it doesn't exist.</param>
        /// <param name="absoluteExpirationRelativeToNow">The expiration time relative to now, or null for no expiration.</param>
        /// <returns>The value from the cache or the factory.</returns>
        public T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (_cache.TryGetValue(key, out object existingValue) && existingValue is T typedValue)
            {
                return typedValue;
            }

            T newValue = factory();

            var options = new MemoryCacheEntryOptions();
            if (absoluteExpirationRelativeToNow.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow.Value;
            }

            _cache.Set(key, newValue, options);
            return newValue;
        }

        /// <summary>
        /// Gets a value from the cache, or adds it asynchronously if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of the value to get or add.</typeparam>
        /// <param name="key">The key of the value to get or add.</param>
        /// <param name="factory">A function that asynchronously produces the value if it doesn't exist.</param>
        /// <param name="absoluteExpirationRelativeToNow">The expiration time relative to now, or null for no expiration.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the value from the cache or the factory.</returns>
        public async Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpirationRelativeToNow = null)
        {
            if (_cache.TryGetValue(key, out object existingValue) && existingValue is T typedValue)
            {
                return typedValue;
            }

            T newValue = await factory();

            var options = new MemoryCacheEntryOptions();
            if (absoluteExpirationRelativeToNow.HasValue)
            {
                options.AbsoluteExpirationRelativeToNow = absoluteExpirationRelativeToNow.Value;
            }

            _cache.Set(key, newValue, options);
            return newValue;
        }

        /// <summary>
        /// Determines whether the cache contains a value with the specified key.
        /// </summary>
        /// <param name="key">The key to check for.</param>
        /// <returns>True if the cache contains a value with the specified key, false otherwise.</returns>
        public bool Contains(string key)
        {
            return _cache.TryGetValue(key, out _);
        }

        /// <summary>
        /// Tries to get a value from the cache.
        /// </summary>
        /// <typeparam name="T">The type of the value to get.</typeparam>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">The value, or default(T) if not found.</param>
        /// <returns>True if the value was found, false otherwise.</returns>
        public bool TryGetValue<T>(string key, out T value)
        {
            if (_cache.TryGetValue(key, out object existingValue) && existingValue is T typedValue)
            {
                value = typedValue;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Removes a value from the cache.
        /// </summary>
        /// <param name="key">The key of the value to remove.</param>
        /// <returns>True if the value was removed, false if it didn't exist.</returns>
        public bool Remove(string key)
        {
            if (_cache.TryGetValue(key, out _))
            {
                _cache.Remove(key);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears all values from the cache.
        /// </summary>
        public void Clear()
        {
            // MemoryCache doesn't have a direct Clear method, so we dispose and recreate it
            var oldCache = _cache;
            _cache = new Microsoft.Extensions.Caching.Memory.MemoryCache(new MemoryCacheOptions());

            oldCache.Dispose();
        }

        /// <summary>
        /// Disposes the cache.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Disposes the cache.
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