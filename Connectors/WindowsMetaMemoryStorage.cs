using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StorageLib.Abstractions;

namespace StorageLib.Connectors
{
    /// <summary>
    /// Windows metadata memory storage that automatically provides Windows-specific system files
    /// such as autorun.inf and desktop.ini for drive customization.
    /// This storage is read-only by design and pre-populated with system files.
    /// </summary>
    public class WindowsMetaMemoryStorage : MemoryStorage
    {
        private readonly ILogger<WindowsMetaMemoryStorage>? _logger;
        private readonly string _driveLabel;
        private readonly string _iconPath;
        private bool _isReadOnly = true; // This storage is read-only by design, but configurable
        private bool _configuredReadOnly = true; // Read-only setting from configuration
        
        /// <summary>
        /// Initializes a new instance of WindowsMetaMemoryStorage with Windows metadata files.
        /// </summary>
        /// <param name="driveLabel">The label/name to display for the drive in Windows Explorer</param>
        /// <param name="iconPath">Path to the icon file to use for the drive (relative to drive root)</param>
        /// <param name="readOnly">Whether this storage should be read-only (true by default for metadata safety)</param>
        /// <param name="logger">Optional logger for debugging</param>
        public WindowsMetaMemoryStorage(string driveLabel = "FolderMagic Drive", string iconPath = "foldermagic.ico", bool readOnly = true, ILogger<WindowsMetaMemoryStorage>? logger = null) 
            : base(logger)
        {
            _logger = logger;
            _driveLabel = driveLabel;
            _iconPath = iconPath;
            
            // Set default limit for Windows metadata storage (10MB should be more than enough)
            // This prevents integer overflow issues when summing with other storage capacities
            LimitStorage = 10 * 1024 * 1024; // 10 MB = 10,485,760 bytes
            
            // Temporary debug - use exceptions since logger might be null
            try
            {
                Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage constructor called with driveLabel='{driveLabel}', iconPath='{iconPath}' - INSTANCE: {this.GetHashCode()}");
                Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage set storage limit to {LimitStorage} bytes (10MB) - INSTANCE: {this.GetHashCode()}");
                
                // Initialize the metadata files synchronously to avoid race conditions
                Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage starting synchronous initialization - INSTANCE: {this.GetHashCode()}");
                InitializeMetadataFilesAsync().GetAwaiter().GetResult();
                Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage synchronous initialization completed successfully - INSTANCE: {this.GetHashCode()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] WindowsMetaMemoryStorage constructor failed: {ex.Message}");
                Console.WriteLine($"[ERROR] Stack trace: {ex.StackTrace}");
                throw new InvalidOperationException($"WindowsMetaMemoryStorage initialization failed: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Creates a new WindowsMetaMemoryStorage instance for the specified MagicSetup.
        /// This static factory method allows passing the MagicSetup name as the drive label.
        /// </summary>
        /// <param name="setupName">Name of the MagicSetup to use as drive label</param>
        /// <returns>Configured WindowsMetaMemoryStorage instance</returns>
        public static WindowsMetaMemoryStorage ForMagicSetup(string setupName)
        {
            return new WindowsMetaMemoryStorage($"{setupName} Drive", "foldermagic.ico");
        }

        /// <summary>
        /// Override the Type property to return our new storage type.
        /// </summary>
        public override EnumStorageType Type
        {
            get => EnumStorageType.WindowsMetaMemory;
            set { } // Ignore attempts to change the type
        }

        /// <summary>
        /// Override FileExistsAsync to add debug logging.
        /// </summary>
        public override async Task<bool> FileExistsAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage FileExistsAsync called for: '{filePath}' - INSTANCE: {this.GetHashCode()}");
            var result = await base.FileExistsAsync(filePath, cancellationToken);
            Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage FileExistsAsync result for '{filePath}': {result} - INSTANCE: {this.GetHashCode()}");
            return result;
        }

        /// <summary>
        /// Override GetFileInfoAsync to add debug logging.
        /// </summary>
        public override async Task<FileObject> GetFileInfoAsync(string filePath, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage GetFileInfoAsync called for: '{filePath}' - INSTANCE: {this.GetHashCode()}");
            try
            {
                var result = await base.GetFileInfoAsync(filePath, cancellationToken);
                Console.WriteLine($"[DEBUG] WindowsMetaMemoryStorage GetFileInfoAsync success for '{filePath}' - INSTANCE: {this.GetHashCode()}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] WindowsMetaMemoryStorage GetFileInfoAsync failed for '{filePath}': {ex.Message} - INSTANCE: {this.GetHashCode()}");
                throw;
            }
        }

        /// <summary>
        /// Creates and populates the Windows metadata files in memory.
        /// </summary>
        private async Task InitializeMetadataFilesAsync()
        {
            try
            {
                _logger?.LogInformation("WindowsMetaMemoryStorage: Starting metadata files initialization");
                
                // Temporarily disable read-only to populate initial files
                _isReadOnly = false;

                // Initialize the base memory storage first
                _logger?.LogDebug("WindowsMetaMemoryStorage: Initializing base MemoryStorage");
                await InitializeAsync("memory://", "/", CancellationToken.None);

                // Create autorun.inf
                _logger?.LogDebug("WindowsMetaMemoryStorage: Creating /autorun.inf");
                var autorunContent = CreateAutorunContent();
                await WriteFileInternal("/autorun.inf", Encoding.UTF8.GetBytes(autorunContent));
                _logger?.LogInformation("WindowsMetaMemoryStorage: Successfully created /autorun.inf");

                // Create desktop.ini
                _logger?.LogDebug("WindowsMetaMemoryStorage: Creating /desktop.ini");
                var desktopIniContent = CreateDesktopIniContent();
                await WriteFileInternal("/desktop.ini", Encoding.UTF8.GetBytes(desktopIniContent));
                _logger?.LogInformation("WindowsMetaMemoryStorage: Successfully created /desktop.ini");

                // Create a simple FolderMagic icon (embedded as text-based ICO)
                _logger?.LogDebug("WindowsMetaMemoryStorage: Creating /{IconPath}", _iconPath);
                var iconContent = CreateSimpleIcon();
                await WriteFileInternal($"/{_iconPath}", iconContent);
                _logger?.LogInformation("WindowsMetaMemoryStorage: Successfully created /{IconPath}", _iconPath);

                // Restore read-only state
                _isReadOnly = true;
                
                _logger?.LogInformation("WindowsMetaMemoryStorage: Metadata files initialization completed successfully");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "WindowsMetaMemoryStorage: Failed to create Windows metadata files");
                // Re-throw the exception so we can see what's actually failing
                throw new InvalidOperationException($"Failed to initialize WindowsMetaMemoryStorage metadata files: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Creates the content for autorun.inf file.
        /// </summary>
        /// <returns>autorun.inf file content</returns>
        private string CreateAutorunContent()
        {
            return $@"[autorun]
icon={_iconPath}
label={_driveLabel}
action=Open {_driveLabel}
open=explorer.exe
";
        }

        /// <summary>
        /// Creates the content for desktop.ini file.
        /// </summary>
        /// <returns>desktop.ini file content</returns>
        private string CreateDesktopIniContent()
        {
            return $@"[.ShellClassInfo]
IconResource={_iconPath},0
InfoTip={_driveLabel} - Virtual file system powered by FolderMagic
LocalizedResourceName={_driveLabel}
";
        }

        /// <summary>
        /// Creates a simple embedded icon for FolderMagic.
        /// This is a minimal 16x16 ICO file representing a folder with magic sparkles.
        /// </summary>
        /// <returns>ICO file bytes</returns>
        private byte[] CreateSimpleIcon()
        {
            var iconData = new List<byte>();
            
            // Calculate sizes
            const int width = 16;
            const int height = 16;
            const int bitsPerPixel = 32;
            const int pixelDataSize = width * height * 4; // 1024 bytes
            const int maskDataSize = width * height / 8;  // 32 bytes (1 bit per pixel)
            const int bitmapInfoHeaderSize = 40;
            const int totalImageSize = bitmapInfoHeaderSize + pixelDataSize + maskDataSize; // 1096 bytes
            
            // ICO header (6 bytes)
            iconData.AddRange(new byte[] { 0x00, 0x00 }); // Reserved (must be 0)
            iconData.AddRange(new byte[] { 0x01, 0x00 }); // Image type (1 = ICO)
            iconData.AddRange(new byte[] { 0x01, 0x00 }); // Number of images (1)
            
            // Image directory entry (16 bytes)
            iconData.Add((byte)width);  // Width (16 pixels)
            iconData.Add((byte)height); // Height (16 pixels)
            iconData.Add(0x00); // Color count (0 = no palette)
            iconData.Add(0x00); // Reserved
            iconData.AddRange(BitConverter.GetBytes((ushort)1)); // Color planes
            iconData.AddRange(BitConverter.GetBytes((ushort)bitsPerPixel)); // Bits per pixel (32-bit)
            iconData.AddRange(BitConverter.GetBytes(totalImageSize)); // Image size
            iconData.AddRange(BitConverter.GetBytes(22)); // Image offset (6 + 16 = 22 bytes)
            
            // BITMAPINFOHEADER (40 bytes) - REQUIRED for ICO format
            iconData.AddRange(BitConverter.GetBytes(bitmapInfoHeaderSize)); // biSize
            iconData.AddRange(BitConverter.GetBytes(width));               // biWidth
            iconData.AddRange(BitConverter.GetBytes(height * 2));          // biHeight (doubled for ICO)
            iconData.AddRange(BitConverter.GetBytes((ushort)1));           // biPlanes
            iconData.AddRange(BitConverter.GetBytes((ushort)bitsPerPixel)); // biBitCount
            iconData.AddRange(BitConverter.GetBytes(0));                   // biCompression (BI_RGB)
            iconData.AddRange(BitConverter.GetBytes(pixelDataSize));       // biSizeImage
            iconData.AddRange(BitConverter.GetBytes(0));                   // biXPelsPerMeter
            iconData.AddRange(BitConverter.GetBytes(0));                   // biYPelsPerMeter
            iconData.AddRange(BitConverter.GetBytes(0));                   // biClrUsed
            iconData.AddRange(BitConverter.GetBytes(0));                   // biClrImportant
            
            // Pixel data (1024 bytes) - stored bottom-to-top in BGRA format
            var bitmapData = new byte[pixelDataSize];
            
            // Fill with a simple folder pattern (BGRA format, bottom-to-top)
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // ICO format stores pixels bottom-to-top, so flip Y coordinate
                    int flippedY = height - 1 - y;
                    int offset = (flippedY * width + x) * 4;
                    
                    // Create a simple folder shape
                    if ((y >= 4 && y <= 12) && (x >= 2 && x <= 13))
                    {
                        // Folder body - blue (BGRA format)
                        bitmapData[offset + 0] = 0xFF; // Blue
                        bitmapData[offset + 1] = 0x80; // Green  
                        bitmapData[offset + 2] = 0x50; // Red
                        bitmapData[offset + 3] = 0xFF; // Alpha
                    }
                    else if ((y >= 2 && y <= 4) && (x >= 2 && x <= 8))
                    {
                        // Folder tab - lighter blue (BGRA format)
                        bitmapData[offset + 0] = 0xFF; // Blue
                        bitmapData[offset + 1] = 0xA0; // Green
                        bitmapData[offset + 2] = 0x80; // Red
                        bitmapData[offset + 3] = 0xFF; // Alpha
                    }
                    else
                    {
                        // Transparent background
                        bitmapData[offset + 0] = 0x00; // Blue
                        bitmapData[offset + 1] = 0x00; // Green
                        bitmapData[offset + 2] = 0x00; // Red
                        bitmapData[offset + 3] = 0x00; // Alpha (fully transparent)
                    }
                }
            }
            
            iconData.AddRange(bitmapData);
            
            // AND mask (32 bytes) - 1 bit per pixel for transparency
            // Since we're using 32-bit BGRA with alpha channel, this can be all zeros
            var maskData = new byte[maskDataSize];
            iconData.AddRange(maskData);
            
            return iconData.ToArray();
        }

        /// <summary>
        /// Override to ensure this storage remains read-only after initialization.
        /// </summary>
        public override async Task WriteAsync(string path, IntPtr fileHandle, long offset, long size, IntPtr buffer, CancellationToken cancellationToken = default)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("WindowsMetaMemoryStorage is read-only. System files cannot be modified.");
            }
            
            await base.WriteAsync(path, fileHandle, offset, size, buffer, cancellationToken);
        }

        /// <summary>
        /// Override to ensure directories cannot be created after initialization.
        /// </summary>
        public override async Task CreateDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("WindowsMetaMemoryStorage is read-only. Directories cannot be created.");
            }
            
            await base.CreateDirectoryAsync(path, cancellationToken);
        }

        /// <summary>
        /// Override to ensure files cannot be deleted after initialization.
        /// </summary>
        public override async Task DeleteAsync(string path, CancellationToken cancellationToken = default)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("WindowsMetaMemoryStorage is read-only. Files cannot be deleted.");
            }
            
            await base.DeleteAsync(path, cancellationToken);
        }

        /// <summary>
        /// Override to ensure directories cannot be deleted after initialization.
        /// </summary>
        public override async Task DeleteDirectoryAsync(string path, CancellationToken cancellationToken = default)
        {
            if (_isReadOnly)
            {
                throw new InvalidOperationException("WindowsMetaMemoryStorage is read-only. Directories cannot be deleted.");
            }
            
            await base.DeleteDirectoryAsync(path, cancellationToken);
        }

        /// <summary>
        /// Internal method to write files during initialization (bypasses read-only check).
        /// </summary>
        private async Task WriteFileInternal(string path, byte[] data)
        {
            Console.WriteLine($"[DEBUG] WriteFileInternal called for path='{path}', dataLength={data.Length}");
            
            try
            {
                // Create a temporary memory stream and use the base MemoryStorage write functionality
                using var stream = new MemoryStream(data);
                
                // Use OpenAsync to create the file, then write to it
                var flags = OpenFlags.Create | OpenFlags.WriteOnly | OpenFlags.Truncate;
                Console.WriteLine($"[DEBUG] Opening file for writing: path='{path}', flags={flags}");
                var handle = await base.OpenAsync(path, flags, CancellationToken.None);
                Console.WriteLine($"[DEBUG] File opened successfully: path='{path}', handle={handle}");
                
                try
                {
                    // Use pinned memory to write the data
                    var pinnedData = GCHandle.Alloc(data, GCHandleType.Pinned);
                    try
                    {
                        Console.WriteLine($"[DEBUG] Writing data to file: path='{path}', length={data.Length}");
                        await base.WriteAsync(path, handle, 0, data.Length, pinnedData.AddrOfPinnedObject(), CancellationToken.None);
                        Console.WriteLine($"[DEBUG] Data written successfully: path='{path}'");
                    }
                    finally
                    {
                        pinnedData.Free();
                    }
                }
                finally
                {
                    Console.WriteLine($"[DEBUG] Closing file: path='{path}'");
                    await base.CloseAsync(path, handle, CancellationToken.None);
                    Console.WriteLine($"[DEBUG] File closed successfully: path='{path}'");
                }
                
                // Verify the file was created by checking if it exists
                Console.WriteLine($"[DEBUG] Verifying file creation: path='{path}'");
                var exists = await base.FileExistsAsync(path, CancellationToken.None);
                Console.WriteLine($"[INFO] File creation verification for '{path}': exists={exists}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR] WriteFileInternal failed for path='{path}': {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Override InitializeAsync to prevent re-initialization that would clear our metadata files.
        /// WindowsMetaMemoryStorage initializes itself in the constructor, so additional calls should be ignored.
        /// </summary>
        public override async Task InitializeAsync(string connectionString, string baseFolderOrContainer, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[INFO] WindowsMetaMemoryStorage.InitializeAsync called with connectionString='{connectionString}', baseFolderOrContainer='{baseFolderOrContainer}' - INSTANCE: {this.GetHashCode()}");
            
            // Check if we're already initialized (have our metadata files)
            try
            {
                if (await base.FileExistsAsync("/desktop.ini", cancellationToken))
                {
                    Console.WriteLine($"[INFO] WindowsMetaMemoryStorage already initialized with metadata files - skipping re-initialization - INSTANCE: {this.GetHashCode()}");
                    return; // Skip re-initialization to preserve our files
                }
            }
            catch
            {
                // If FileExistsAsync fails, we're probably not initialized yet
            }
            
            Console.WriteLine($"[WARNING] WindowsMetaMemoryStorage not yet initialized or files missing - proceeding with initialization - INSTANCE: {this.GetHashCode()}");
            await base.InitializeAsync(connectionString, baseFolderOrContainer, cancellationToken);
        }

        /// <summary>
        /// Gets a description of this storage connector.
        /// </summary>
        /// <returns>Description string</returns>
        public override string ToString()
        {
            return $"WindowsMetaMemoryStorage: {_driveLabel} (provides autorun.inf, desktop.ini)";
        }
    }
} 