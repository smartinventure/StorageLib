# FolderMagic Caching System

The FolderMagic caching system provides a flexible and easy-to-use caching infrastructure that allows you to create and manage multiple named caches within your application.

## Overview

The caching system is designed with the following components:

- **ICache**: Interface that defines the core caching operations (get, set, remove, clear).
- **MemoryCache**: An implementation of ICache using Microsoft's memory cache.
- **ICacheManager**: Interface for managing multiple named caches.
- **CacheManager**: Implementation of ICacheManager.

Each MagicSetup instance has its own CacheManager, allowing different setups to have isolated caches.

## Key Features

- Create multiple named caches per MagicSetup
- Thread-safe operations
- Support for both synchronous and asynchronous value factory patterns
- Configurable expiration policies (absolute and sliding)
- Automatic cache entry eviction tracking

## Usage

### Basic Cache Operations

```csharp
// Get or create a cache for a specific purpose
ICache openSlideCache = magicSetup.GetCache("openslide");

// Add or retrieve an item from the cache
var result = openSlideCache.GetOrAdd("key", () => {
    // Expensive operation to get the value
    return ComputeExpensiveValue();
}, 
absoluteExpiration: DateTimeOffset.Now.AddHours(1)); // Cache for 1 hour
```

### Asynchronous Operations

```csharp
// Asynchronously get or add an item
var result = await cache.GetOrAddAsync("key", async () => {
    // Expensive async operation
    return await FetchDataAsync();
});
```

### Cache Management

```csharp
// List all caches
IEnumerable<string> cacheNames = magicSetup.CacheManager.GetCacheNames();

// Clear a specific cache
magicSetup.GetCache("thumbnails").Clear();

// Remove a cache entirely
magicSetup.CacheManager.RemoveCache("temporaryCache");

// Clear all caches
magicSetup.CacheManager.ClearAllCaches();
```

## Example Use Cases

### OpenSlide Image Caching

Cache tiles from large digital slide images to improve performance:

```csharp
string cacheKey = $"{slidePath}:{x}:{y}:{width}:{height}";
var tileImage = await openSlideCache.GetOrAddAsync(cacheKey, async () => {
    // Expensive OpenSlide operation to extract the tile
    return await ExtractTileFromSlideAsync(slidePath, x, y, width, height);
});
```

### File Content Caching

Cache file contents to reduce disk I/O:

```csharp
var fileContent = fileCache.GetOrAdd(filePath, () => {
    // Read from disk
    return File.ReadAllText(filePath);
}, 
slidingExpiration: TimeSpan.FromMinutes(10));
```

## Implementation Guidance

1. **Cache Naming**: Use descriptive names for caches to keep them organized.
2. **Cache Keys**: Create unique and consistent keys that represent the cached item.
3. **Expiration Policies**: Choose appropriate expiration strategies based on your data's volatility.
4. **Memory Considerations**: Be mindful of cache sizes for memory-intensive applications.

## Extending the System

To implement a different caching backend (e.g., Redis, distributed cache):

1. Create a new class implementing the ICache interface
2. Update the CacheManager.GetOrCreateCache method to use your new implementation

## Performance Considerations

- Use the cache for expensive operations that are frequently repeated
- Choose appropriate cache expiration times to balance memory usage with performance
- For very large items, consider caching metadata or references instead of the full item 