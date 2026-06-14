using System;
using System.Threading.Tasks;

namespace StorageLib.Caching
{
    /// <summary>
    /// Defines a cache that can store and retrieve values.
    /// </summary>
    public interface ICache
    {
        /// <summary>
        /// Gets the name of the cache.
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets a value from the cache.
        /// </summary>
        /// <typeparam name="T">The type of the value to get.</typeparam>
        /// <param name="key">The key of the value to get.</param>
        /// <returns>The value, or default(T) if not found.</returns>
        T Get<T>(string key);

        /// <summary>
        /// Sets a value in the cache.
        /// </summary>
        /// <typeparam name="T">The type of the value to set.</typeparam>
        /// <param name="key">The key of the value to set.</param>
        /// <param name="value">The value to set.</param>
        /// <param name="absoluteExpirationRelativeToNow">The expiration time relative to now, or null for no expiration.</param>
        void Set<T>(string key, T value, TimeSpan? absoluteExpirationRelativeToNow = null);

        /// <summary>
        /// Gets a value from the cache or adds it if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of the value to get or add.</typeparam>
        /// <param name="key">The key of the value to get or add.</param>
        /// <param name="factory">A function that produces the value if it doesn't exist.</param>
        /// <param name="absoluteExpirationRelativeToNow">The expiration time relative to now, or null for no expiration.</param>
        /// <returns>The value from the cache or the factory.</returns>
        T GetOrAdd<T>(string key, Func<T> factory, TimeSpan? absoluteExpirationRelativeToNow = null);

        /// <summary>
        /// Gets a value from the cache, or adds it asynchronously if it doesn't exist.
        /// </summary>
        /// <typeparam name="T">The type of the value to get or add.</typeparam>
        /// <param name="key">The key of the value to get or add.</param>
        /// <param name="factory">A function that asynchronously produces the value if it doesn't exist.</param>
        /// <param name="absoluteExpirationRelativeToNow">The expiration time relative to now, or null for no expiration.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the value from the cache or the factory.</returns>
        Task<T> GetOrAddAsync<T>(string key, Func<Task<T>> factory, TimeSpan? absoluteExpirationRelativeToNow = null);

        /// <summary>
        /// Determines whether the cache contains a value with the specified key.
        /// </summary>
        /// <param name="key">The key to check for.</param>
        /// <returns>True if the cache contains a value with the specified key, false otherwise.</returns>
        bool Contains(string key);

        /// <summary>
        /// Tries to get a value from the cache.
        /// </summary>
        /// <typeparam name="T">The type of the value to get.</typeparam>
        /// <param name="key">The key of the value to get.</param>
        /// <param name="value">The value, or default(T) if not found.</param>
        /// <returns>True if the value was found, false otherwise.</returns>
        bool TryGetValue<T>(string key, out T value);

        /// <summary>
        /// Removes a value from the cache.
        /// </summary>
        /// <param name="key">The key of the value to remove.</param>
        /// <returns>True if the value was removed, false if it didn't exist.</returns>
        bool Remove(string key);

        /// <summary>
        /// Clears all values from the cache.
        /// </summary>
        void Clear();
    }
}