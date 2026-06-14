using Microsoft.Extensions.Logging;
using StorageLib.Abstractions;
using StorageLib.Connectors;
using StorageLib.Connectors.OpenSlide;
using StorageLib.OpenSlide;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;

namespace StorageLib.Connectors.OpenSlide
{
    /// <summary>
    /// Storage implementation for OpenSlide that provides a slide pyramid representation of slides.
    /// Inherits from MemoryStorage to leverage its virtual filesystem capabilities.
    /// </summary>
    public class OpenSlideStorage : MemoryStorage
    {
        // Caching disabled for now - using direct storage instead
        private readonly ConcurrentDictionary<string, SlideInfo> _slides = new ConcurrentDictionary<string, SlideInfo>();
        private readonly Microsoft.Extensions.Logging.ILogger _logger;
        private readonly DeepZoomHandler _deepZoomHandler;
        private bool _isDisposed;
        private readonly string _layerName;
        private readonly ConcurrentDictionary<string, SlideInfo> _openSlides = new ConcurrentDictionary<string, SlideInfo>();

        // Regular expressions for pattern matching
        private static readonly Regex _tilePathRegex = new(@"(?<slidePath>/.*?)/(?<slideName>[^/]+)_files/(?<level>\d+)/(?<column>\d+)_(?<row>\d+)\.(?<ext>\w+)$", RegexOptions.Compiled);
        private static readonly Regex _levelDirRegex = new(@"(?<slidePath>/.*?)/(?<slideName>[^/]+)_files/(?<level>\d+)/?$", RegexOptions.Compiled);
        private static readonly Regex _filesRootRegex = new(@"(?<slidePath>/.*?)/(?<slideName>[^/]+)_files/?$", RegexOptions.Compiled);
        private static readonly Regex _dziFileRegex = new(@"(?<slidePath>/.*?)/(?<slideName>[^/]+)\.dzi$", RegexOptions.Compiled);
        private static readonly Regex _metadataFileRegex = new(@"(?<slidePath>/.*?)/(?<slideName>[^/]+)_metadata\.json$", RegexOptions.Compiled);

        /// <summary>
        /// Gets or sets the storage type.
        /// </summary>
        public override EnumStorageType Type
        {
            get => EnumStorageType.OpenSlide;
            set { } // Empty setter - ignores the value
        }

        /// <summary>
        /// Gets or sets the connection string.
        /// </summary>
        public override string ConnectionString
        {
            get => "openslide://";
            set { } // Empty setter - ignores the value
        }

        /// <summary>
        /// Initializes a new instance of the OpenSlideStorage class.
        /// </summary>
        /// <param name="logger">The logger to use.</param>
        /// <param name="layerName">The name of the layer for cache isolation. Default is "openslide".
        /// This allows each OpenSlideStorage instance to have its own isolated cache space.</param>
        public OpenSlideStorage(Microsoft.Extensions.Logging.ILogger logger, string layerName = "openslide") : base()
        {
            // Caching disabled for now - will be implemented at storage level later
            _logger = logger;
            _deepZoomHandler = new DeepZoomHandler(logger);
            _layerName = layerName;
        }

        /// <summary>
        /// Convenience constructor that creates default logger.
        /// </summary>
        /// <param name="layerName">The name of the layer for cache isolation.</param>
        public OpenSlideStorage(string layerName = "openslide") : this(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, layerName)
        {
        }

        #region Overridden Methods from MemoryStorage

        /// <summary>
        /// Get the total storage capacity asynchronously
        /// We override this for OpenSlide to provide fixed values
        /// </summary>
        public override async Task<long> GetTotalCapacityAsync(CancellationToken cancellationToken = default)
        {
            // OpenSlide is read-only, so we provide a reasonable fixed capacity value
            // 1 GB should be enough for slide data that's cached
            return await Task.FromResult(1024 * 1024 * 1024);
        }

        /// <summary>
        /// Get the available free space asynchronously
        /// We override this for OpenSlide to provide fixed values
        /// </summary>
        public override async Task<long> GetAvailableFreeSpaceAsync(CancellationToken cancellationToken = default)
        {
            // OpenSlide is read-only, so we provide a reasonable fixed free space
            // 512 MB as available (half of the total capacity)
            return await Task.FromResult(512 * 1024 * 1024);
        }

        /// <summary>
        /// Check if a file exists by calculating its existence based on patterns
        /// </summary>
        public override async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            // First check if it's a real file in our memory dictionary
            if (await base.FileExistsAsync(filePath, cancellationToken))
            {
                return true;
            }

            // Then check if it's a virtual slide file
            return IsVirtualFile(filePath);
        }

        /// <summary>
        /// Check if a directory exists by calculating its existence based on patterns
        /// </summary>
        public override async Task<bool> DirectoryExistsAsync(string directoryPath, CancellationToken cancellationToken = default)
        {
            // First check if it's a real directory in our memory dictionary
            if (await base.DirectoryExistsAsync(directoryPath, cancellationToken))
            {
                return true;
            }

            // Then check if it's a virtual slide directory
            return IsVirtualDirectory(directoryPath);
        }

        /// <summary>
        /// Read file data by generating content on-demand for virtual files
        /// </summary>
        public override async Task<byte[]> ReadFileAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                // First try to read from the base memory storage
                try
                {
                    return await base.ReadFileAsync(path, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    // If file not found in base storage, continue with virtual file checks
                }

                // DZI file
                if (IsDziFile(path, out var slideInfo))
                {
                    return _deepZoomHandler.GenerateDziXml(slideInfo);
                }

                // Metadata file
                if (IsMetadataFile(path, out slideInfo))
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    return JsonSerializer.SerializeToUtf8Bytes(slideInfo, options);
                }

                // Tile file
                if (IsTileFile(path, out slideInfo, out var level, out var column, out var row))
                {
                    // Generate tile directly (caching disabled for now)
                    return await _deepZoomHandler.GenerateTileAsync(slideInfo, level, column, row);
                }

                throw new FileNotFoundException($"File not found: {path}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error reading file {path}: {ex.Message}");
                throw;
            }
        }
/*
        /// <summary>
        /// Override read to handle virtual files
        /// </summary>
        public override async Task ReadAsync(IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            try
            {
                if (IsVirtualFile(path))
                {
                    var data = await ReadFileAsync(path, cancellationToken);
                    if (data != null && data.Length > 0)
                    {
                        int bytesToCopy = (int)Math.Min(size, data.Length - offset);
                        if (bytesToCopy > 0)
                        {
                            Marshal.Copy(data, (int)offset, buffer, bytesToCopy);
                        }
                    }
                    return;
                }

                // Fall back to base implementation for real files
                await base.ReadAsync(path, offset, size, buffer, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error reading from {path}: {ex.Message}");
                throw;
            }
        }

      */

        /// <summary>
        /// Read directory contents (files and subdirectories), including virtual ones
        /// </summary>
        public override async Task<IEnumerable<FileObject>> ReadDirAsync(string path, bool readOnly = true, CancellationToken cancellationToken = default)
        {
            // Get real entries from base storage
            var entries = (await base.ReadDirAsync(path, readOnly, cancellationToken)).ToList();

            // Check if this is a slide directory
            if (IsSlideRootDirectory(path, out var slideInfo))
            {
                // Add DZI file
                string dziName = $"{Path.GetFileName(slideInfo.VirtualPath)}.dzi";
                string dziPath = Path.Combine(path, dziName);
                if (!entries.Any(e => e.VirtualPath == dziPath))
                {
                    entries.Add(new FileObject(dziPath, this)
                    {
                        IsDirectory = false,
                        Filename = dziName,
                        RealPath = dziPath,
                        VirtualPath = dziPath,
                        Size = _deepZoomHandler.GenerateDziXml(slideInfo).Length,
                        LastModified = DateTime.UtcNow,
                        CreationTime = DateTime.UtcNow,
                        LastAccessTime = DateTime.UtcNow,
                        IsReadOnly = true,
                        Mode = 0644  // Standard file permissions
                    });
                }

                // Add metadata file
                string metadataName = $"{Path.GetFileName(slideInfo.VirtualPath)}_metadata.json";
                string metadataPath = Path.Combine(path, metadataName);
                if (!entries.Any(e => e.VirtualPath == metadataPath))
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var metadataSize = JsonSerializer.SerializeToUtf8Bytes(slideInfo, options).Length;

                    entries.Add(new FileObject(metadataPath, this)
                    {
                        IsDirectory = false,
                        Filename = metadataName,
                        RealPath = metadataPath,
                        VirtualPath = metadataPath,
                        Size = metadataSize,
                        LastModified = DateTime.UtcNow,
                        CreationTime = DateTime.UtcNow,
                        LastAccessTime = DateTime.UtcNow,
                        IsReadOnly = true,
                        Mode = 0644  // Standard file permissions
                    });
                }

                // Add _files directory
                string filesDir = $"{Path.GetFileName(slideInfo.VirtualPath)}_files";
                string filesDirPath = Path.Combine(path, filesDir);
                if (!entries.Any(e => e.VirtualPath == filesDirPath))
                {
                    entries.Add(new FileObject(filesDirPath, this)
                    {
                        IsDirectory = true,
                        Filename = filesDir,
                        RealPath = filesDirPath,
                        VirtualPath = filesDirPath,
                        Size = 0,
                        LastModified = DateTime.UtcNow,
                        CreationTime = DateTime.UtcNow,
                        LastAccessTime = DateTime.UtcNow,
                        IsReadOnly = true,
                        Mode = 0755  // Standard directory permissions
                    });
                }
            }
            // Check if this is a _files directory
            else if (IsFilesRootDirectory(path, out slideInfo))
            {
                // Add level directories
                for (int level = 0; level < slideInfo.LevelCount; level++)
                {
                    string levelDir = level.ToString();
                    string levelPath = Path.Combine(path, levelDir);
                    if (!entries.Any(e => e.VirtualPath == levelPath))
                    {
                        entries.Add(new FileObject(levelPath, this)
                        {
                            IsDirectory = true,
                            Filename = levelDir,
                            RealPath = levelPath,
                            VirtualPath = levelPath,
                            Size = 0,
                            LastModified = DateTime.UtcNow,
                            CreationTime = DateTime.UtcNow,
                            LastAccessTime = DateTime.UtcNow,
                            IsReadOnly = true,
                            Mode = 0755  // Standard directory permissions
                        });
                    }
                }
            }
            // Check if this is a level directory
            else if (IsLevelDirectory(path, out slideInfo, out var level))
            {
                // Find the level info
                var levelInfo = slideInfo.Levels.FirstOrDefault(l => l.Level == level);
                if (levelInfo != null)
                {
                    string format = GetFormatExtension(slideInfo.Format);

                    // Add tile files - we're not actually generating all possible tiles
                    // Instead, we'll generate some representative tiles (up to a limit)
                    // The full set will be calculated on demand
                    int maxTilesToShow = 50; // Limit number of visible tiles in directory listing
                    int tilesToShow = Math.Min(maxTilesToShow, levelInfo.Columns * levelInfo.Rows);

                    // Show some representative tiles distributed across the image
                    if (tilesToShow > 0)
                    {
                        int totalTiles = levelInfo.Columns * levelInfo.Rows;
                        float step = (float)totalTiles / tilesToShow;

                        for (int i = 0; i < tilesToShow; i++)
                        {
                            int tileIndex = (int)(i * step);
                            int row = tileIndex / levelInfo.Columns;
                            int column = tileIndex % levelInfo.Columns;

                            if (row < levelInfo.Rows && column < levelInfo.Columns)
                            {
                                string tileName = $"{column}_{row}.{format}";
                                string tilePath = Path.Combine(path, tileName);

                                if (!entries.Any(e => e.VirtualPath == tilePath))
                                {
                                    // Estimate tile size since we don't want to generate it now
                                    long tileSize = DeepZoomHandler.EstimateTileSize(slideInfo.Format, slideInfo.TileSize, slideInfo.TileSize, slideInfo.JpegQuality);

                                    entries.Add(new FileObject(tilePath, this)
                                    {
                                        IsDirectory = false,
                                        Filename = tileName,
                                        RealPath = tilePath,
                                        VirtualPath = tilePath,
                                        Size = tileSize,
                                        LastModified = DateTime.UtcNow,
                                        CreationTime = DateTime.UtcNow,
                                        LastAccessTime = DateTime.UtcNow,
                                        IsReadOnly = true,
                                        Mode = 0644  // Standard file permissions
                                    });
                                }
                            }
                        }
                    }
                }
            }

            return entries;
        }

        /// <summary>
        /// Get file information for both real and virtual files
        /// </summary>
        public override async Task<FileObject> GetFileInfoAsync(string path, CancellationToken cancellationToken = default)
        {
            try
            {
                // Try base implementation first
                try
                {
                    return await base.GetFileInfoAsync(path, cancellationToken);
                }
                catch (FileNotFoundException)
                {
                    // If not found in base storage, check virtual files
                }

                // Handle virtual files
                if (IsDziFile(path, out var slideInfo))
                {
                    var dziContent = _deepZoomHandler.GenerateDziXml(slideInfo);
                    return new FileObject(path, this)
                    {
                        IsDirectory = false,
                        Filename = Path.GetFileName(path),
                        RealPath = path,
                        VirtualPath = path,
                        Size = dziContent.Length,
                        LastModified = DateTime.UtcNow,
                        CreationTime = DateTime.UtcNow,
                        LastAccessTime = DateTime.UtcNow,
                        IsReadOnly = true,
                        Mode = 0644  // Standard file permissions
                    };
                }

                if (IsMetadataFile(path, out slideInfo))
                {
                    var options = new JsonSerializerOptions { WriteIndented = true };
                    var metadataSize = JsonSerializer.SerializeToUtf8Bytes(slideInfo, options).Length;

                    return new FileObject(path, this)
                    {
                        IsDirectory = false,
                        Filename = Path.GetFileName(path),
                        RealPath = path,
                        VirtualPath = path,
                        Size = metadataSize,
                        LastModified = DateTime.UtcNow,
                        CreationTime = DateTime.UtcNow,
                        LastAccessTime = DateTime.UtcNow,
                        IsReadOnly = true,
                        Mode = 0644  // Standard file permissions
                    };
                }

                if (IsTileFile(path, out slideInfo, out var level, out var column, out var row))
                {
                    long tileSize = DeepZoomHandler.EstimateTileSize(slideInfo.Format, slideInfo.TileSize, slideInfo.TileSize, slideInfo.JpegQuality);

                    return new FileObject(path, this)
                    {
                        IsDirectory = false,
                        Filename = Path.GetFileName(path),
                        RealPath = path,
                        VirtualPath = path,
                        Size = tileSize,
                        LastModified = DateTime.UtcNow,
                        CreationTime = DateTime.UtcNow,
                        LastAccessTime = DateTime.UtcNow,
                        IsReadOnly = true,
                        Mode = 0644  // Standard file permissions
                    };
                }

                if (IsVirtualDirectory(path))
                {
                    return new FileObject(path, this)
                    {
                        IsDirectory = true,
                        Filename = Path.GetFileName(path),
                        RealPath = path,
                        VirtualPath = path,
                        Size = 0,
                        LastModified = DateTime.UtcNow,
                        CreationTime = DateTime.UtcNow,
                        LastAccessTime = DateTime.UtcNow,
                        IsReadOnly = true,
                        Mode = 0755  // Standard directory permissions
                    };
                }

                throw new FileNotFoundException($"File not found: {path}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error getting file info for {path}: {ex.Message}");
                throw;
            }
        }

        #endregion

        #region OpenSlide-specific Methods

        /// <summary>
        /// Process a command to add, update, or remove a slide
        /// </summary>
        public async Task<bool> ProcessCommand(OpenSlideCommand command)
        {
            if (!command.IsValid)
            {
                _logger.LogError($"Invalid OpenSlide command: {command.Operation} for {command.VirtualPath}");
                return false;
            }

            try
            {
                switch (command.Operation)
                {
                    case OpenSlideCommand.OpenSlideOperation.Create:
                    case OpenSlideCommand.OpenSlideOperation.Update:
                        // Load slide properties
                        var slideInfo = await _deepZoomHandler.LoadSlidePropertiesAsync(command.SourcePath, command);
                        if (slideInfo == null)
                        {
                            _logger.LogError($"Failed to load slide: {command.SourcePath}");
                            return false;
                        }

                        // Add/update slide in storage
                        AddOrUpdateSlide(slideInfo);

                        // Create the slide directory in memory storage
                        await CreateSlideStructureAsync(slideInfo);
                        return true;

                    case OpenSlideCommand.OpenSlideOperation.Delete:
                        // Get the slide info
                        var slideToRemove = GetSlideByVirtualPath(command.VirtualPath);
                        if (slideToRemove == null)
                        {
                            _logger.LogWarning($"Slide not found for deletion: {command.VirtualPath}");
                            return false;
                        }

                        // Remove from storage
                        await DeleteSlideStructureAsync(slideToRemove);
                        return true;

                    default:
                        _logger.LogError($"Unsupported OpenSlide operation: {command.Operation}");
                        return false;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error processing OpenSlide command: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Gets all registered slides.
        /// </summary>
        /// <returns>A collection of registered slides.</returns>
        public IEnumerable<SlideInfo> GetAllSlides()
        {
            return _slides.Values;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Creates the necessary directory structure for a slide
        /// </summary>
        private async Task CreateSlideStructureAsync(SlideInfo slideInfo)
        {
            // Normalize paths
            string slidePath = slideInfo.VirtualPath;
            string slideName = Path.GetFileName(slidePath);

            // Create main slide directory
            await CreateDirectoryAsync(slidePath);

            // We don't need to create all the tile files, folders, etc.
            // Just create the main directory structure and let the files be created on demand
        }

        /// <summary>
        /// Deletes the directory structure for a slide
        /// </summary>
        private async Task DeleteSlideStructureAsync(SlideInfo slideInfo)
        {
            if (slideInfo == null) return;

            string slidePath = slideInfo.VirtualPath;

            // Remove from slides collection
            string key = NormalizeVirtualPath(slideInfo.VirtualPath);
            _slides.TryRemove(key, out _);

            // Delete the slide directory and everything in it
            if (await DirectoryExistsAsync(slidePath))
            {
                await DeleteDirectoryAsync(slidePath);
            }
        }

        /// <summary>
        /// Checks if a path is a DZI file and returns the corresponding slide info
        /// </summary>
        private bool IsDziFile(string path, out SlideInfo slideInfo)
        {
            slideInfo = null;

            var match = _dziFileRegex.Match(path);
            if (!match.Success) return false;

            string slidePath = match.Groups["slidePath"].Value;
            string slideName = match.Groups["slideName"].Value;
            string slideDir = Path.Combine(slidePath, slideName);

            slideInfo = GetSlideByVirtualPath(slideDir);
            return slideInfo != null;
        }

        /// <summary>
        /// Checks if a path is a metadata file and returns the corresponding slide info
        /// </summary>
        private bool IsMetadataFile(string path, out SlideInfo slideInfo)
        {
            slideInfo = null;

            var match = _metadataFileRegex.Match(path);
            if (!match.Success) return false;

            string slidePath = match.Groups["slidePath"].Value;
            string slideName = match.Groups["slideName"].Value;
            string slideDir = Path.Combine(slidePath, slideName);

            slideInfo = GetSlideByVirtualPath(slideDir);
            return slideInfo != null;
        }

        /// <summary>
        /// Checks if a path is a tile file and returns the corresponding slide info and tile coordinates
        /// </summary>
        private bool IsTileFile(string path, out SlideInfo slideInfo, out int level, out int column, out int row)
        {
            slideInfo = null;
            level = 0;
            column = 0;
            row = 0;

            var match = _tilePathRegex.Match(path);
            if (!match.Success) return false;

            if (!int.TryParse(match.Groups["level"].Value, out level) ||
                !int.TryParse(match.Groups["column"].Value, out column) ||
                !int.TryParse(match.Groups["row"].Value, out row))
            {
                return false;
            }

            string slidePath = match.Groups["slidePath"].Value;
            string slideName = match.Groups["slideName"].Value;
            string slideDir = Path.Combine(slidePath, slideName);

            slideInfo = GetSlideByVirtualPath(slideDir);
            if (slideInfo == null) return false;

            // Check if level, column, row are valid for this slide
            if (level < 0 || level >= slideInfo.LevelCount) return false;

            // Store the level value in a local variable to avoid "Cannot use ref parameter inside lambda" error
            int levelValue = level;
            var levelInfo = slideInfo.Levels.FirstOrDefault(l => l.Level == levelValue);
            if (levelInfo == null) return false;

            return column >= 0 && column < levelInfo.Columns &&
                   row >= 0 && row < levelInfo.Rows;
        }

        /// <summary>
        /// Checks if a path is a slide root directory and returns the corresponding slide info
        /// </summary>
        private bool IsSlideRootDirectory(string path, out SlideInfo slideInfo)
        {
            slideInfo = GetSlideByVirtualPath(path);
            return slideInfo != null;
        }

        /// <summary>
        /// Checks if a path is a files root directory and returns the corresponding slide info
        /// </summary>
        private bool IsFilesRootDirectory(string path, out SlideInfo slideInfo)
        {
            slideInfo = null;

            var match = _filesRootRegex.Match(path);
            if (!match.Success) return false;

            string slidePath = match.Groups["slidePath"].Value;
            string slideName = match.Groups["slideName"].Value;
            string slideDir = Path.Combine(slidePath, slideName);

            slideInfo = GetSlideByVirtualPath(slideDir);
            return slideInfo != null;
        }

        /// <summary>
        /// Checks if a path is a level directory and returns the corresponding slide info and level
        /// </summary>
        private bool IsLevelDirectory(string path, out SlideInfo slideInfo, out int level)
        {
            slideInfo = null;
            level = 0;

            var match = _levelDirRegex.Match(path);
            if (!match.Success) return false;

            if (!int.TryParse(match.Groups["level"].Value, out level))
            {
                return false;
            }

            string slidePath = match.Groups["slidePath"].Value;
            string slideName = match.Groups["slideName"].Value;
            string slideDir = Path.Combine(slidePath, slideName);

            slideInfo = GetSlideByVirtualPath(slideDir);
            if (slideInfo == null) return false;

            return level >= 0 && level < slideInfo.LevelCount;
        }

        /// <summary>
        /// Checks if a path is a virtual file (DZI, metadata, or tile).
        /// </summary>
        private bool IsVirtualFile(string path)
        {
            return IsDziFile(path, out _) ||
                   IsMetadataFile(path, out _) ||
                   IsTileFile(path, out _, out _, out _, out _);
        }

        /// <summary>
        /// Checks if a path is a virtual directory (any calculated directory that's not stored in the dictionary)
        /// </summary>
        private bool IsVirtualDirectory(string path)
        {
            return IsSlideRootDirectory(path, out _) ||
                   IsFilesRootDirectory(path, out _) ||
                   IsLevelDirectory(path, out _, out _);
        }

        /// <summary>
        /// Gets the file extension for a tile format
        /// </summary>
        private string GetFormatExtension(OpenSlideCommand.TileFormat format)
        {
            return format switch
            {
                OpenSlideCommand.TileFormat.JPEG => "jpg",
                OpenSlideCommand.TileFormat.PNG => "png",
                OpenSlideCommand.TileFormat.BGRA => "bgra",
                _ => "jpg"
            };
        }

        /// <summary>
        /// Gets a slide by its virtual path (without caching)
        /// </summary>
        private SlideInfo GetSlideByVirtualPath(string virtualPath)
        {
            string key = NormalizeVirtualPath(virtualPath);
            _slides.TryGetValue(key, out var slideInfo);
            return slideInfo;
        }

        /// <summary>
        /// Adds or updates a slide in storage (without caching)
        /// </summary>
        public void AddOrUpdateSlide(SlideInfo slideInfo)
        {
            if (slideInfo == null) return;

            string key = NormalizeVirtualPath(slideInfo.VirtualPath);
            _slides[key] = slideInfo;
        }

        private static string NormalizeVirtualPath(string virtualPath)
        {
            if (string.IsNullOrEmpty(virtualPath))
                return string.Empty;
                
            // Simple path normalization - replace backslashes with forward slashes and trim
            return virtualPath.Replace('\\', '/').Trim('/');
        }

        #endregion

        /// <summary>
        /// Disposes the OpenSlideStorage instance
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    // Clean up managed resources
                }

                _isDisposed = true;
            }

            base.Dispose(disposing);
        }

        public Task<IStorage> CloneAsync(string newConnectionString, string newBaseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            // Implementation of CloneAsync method
            throw new NotImplementedException();
        }
    }
}