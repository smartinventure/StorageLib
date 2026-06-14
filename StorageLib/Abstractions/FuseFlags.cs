using System;

namespace StorageLib.Abstractions
{
    /// <summary>
    /// Flags enum for FUSE/POSIX open operations providing type safety
    /// </summary>
    [Flags]
    public enum OpenFlags : int
    {
        // Access mode flags (these are the first 2 bits)
        ReadOnly = 0x0000,          // Open for read-only access
        WriteOnly = 0x0001,         // Open for write-only access
        ReadWrite = 0x0002,         // Open for both reading and writing
        AccessMode = 0x0003,        // Mask for access mode bits
        
        // File creation and status flags
        Create = 0x0040,            // Create file if it doesn't exist
        Exclusive = 0x0080,         // Used with Create, fail if file exists
        Truncate = 0x0200,          // Truncate file to zero length if it exists
        Append = 0x0400,            // Open the file in append mode
        NonBlock = 0x0800,          // Open in non-blocking mode
        Sync = 0x101000,            // Write operations complete only when data is written to disk
        NoFollow = 0x20000,         // Do not follow symbolic links
        Directory = 0x10000,        // Open fails if file is not a directory
        CloseOnExec = 0x80000,      // Close file descriptor on exec()
        Direct = 0x4000,            // Minimize cache effects (bypass buffering)
        LargeFile = 0x2000,         // Allow files larger than 2GB on 32-bit systems
        Sequential = 0x08000000,    // Windows-specific optimization flag
        CreateParentDirectories = 0x200000  // Auto-create missing parent directories (like mkdir -p) - This is invented by FolderMagic to handle non-existing directories in overlaps
    }

    /// <summary>
    /// Constants for FUSE/POSIX open flags representing the standard Linux open(2) flags.
    /// These are universal file system concepts, not specific to any particular product.
    /// </summary>
    public static class FuseFlags
    {
        // Access mode flags (these are the first 2 bits)
        public const int O_RDONLY = (int)OpenFlags.ReadOnly;
        public const int O_WRONLY = (int)OpenFlags.WriteOnly;
        public const int O_RDWR = (int)OpenFlags.ReadWrite;
        public const int O_ACCMODE = (int)OpenFlags.AccessMode;
        
        // File creation and status flags
        public const int O_CREAT = (int)OpenFlags.Create;
        public const int O_EXCL = (int)OpenFlags.Exclusive;
        public const int O_TRUNC = (int)OpenFlags.Truncate;
        public const int O_APPEND = (int)OpenFlags.Append;
        public const int O_NONBLOCK = (int)OpenFlags.NonBlock;
        public const int O_SYNC = (int)OpenFlags.Sync;
        public const int O_NOFOLLOW = (int)OpenFlags.NoFollow;
        public const int O_DIRECTORY = (int)OpenFlags.Directory;
        public const int O_CLOEXEC = (int)OpenFlags.CloseOnExec;
        public const int O_DIRECT = (int)OpenFlags.Direct;
        public const int O_LARGEFILE = (int)OpenFlags.LargeFile;
        public const int O_SEQUENTIAL = (int)OpenFlags.Sequential;
        public const int O_CREATEPARENTS = (int)OpenFlags.CreateParentDirectories;

        /// <summary>
        /// Extracts the access mode from flags
        /// </summary>
        public static int GetAccessMode(int flags)
        {
            return flags & O_ACCMODE;
        }



        /// <summary>
        /// Extracts the access mode from flags (enum version)
        /// </summary>
        public static OpenFlags GetAccessMode(OpenFlags flags)
        {
            return flags & OpenFlags.AccessMode;
        }

        /// <summary>
        /// Checks if read access is requested
        /// </summary>
        public static bool IsReadAccess(int flags)
        {
            int mode = GetAccessMode(flags);
            return mode == O_RDONLY || mode == O_RDWR;
        }

        /// <summary>
        /// Checks if read access is requested (enum version)
        /// </summary>
        public static bool IsReadAccess(OpenFlags flags)
        {
            var mode = GetAccessMode(flags);
            return mode == OpenFlags.ReadOnly || mode == OpenFlags.ReadWrite;
        }

        /// <summary>
        /// Checks if write access is requested
        /// </summary>
        public static bool IsWriteAccess(int flags)
        {
            int mode = GetAccessMode(flags);
            return mode == O_WRONLY || mode == O_RDWR;
        }

        /// <summary>
        /// Checks if write access is requested (enum version)
        /// </summary>
        public static bool IsWriteAccess(OpenFlags flags)
        {
            var mode = GetAccessMode(flags);
            return mode == OpenFlags.WriteOnly || mode == OpenFlags.ReadWrite;
        }

        /// <summary>
        /// Checks if a specific flag is set
        /// </summary>
        public static bool HasFlag(int flags, int flag)
        {
            return (flags & flag) == flag;
        }

        /// <summary>
        /// Checks if a specific flag is set (enum version)
        /// </summary>
        public static bool HasFlag(OpenFlags flags, OpenFlags flag)
        {
            return (flags & flag) == flag;
        }

        /// <summary>
        /// Converts integer FUSE flags to StorageLib OpenFlags enum
        /// </summary>
        /// <param name="flags">Integer FUSE flags</param>
        /// <returns>OpenFlags enum equivalent</returns>
        public static OpenFlags ConvertToOpenFlags(int flags)
        {
            var openFlags = StorageLib.Abstractions.OpenFlags.ReadOnly; // Default

            // Check access mode first
            if (FuseFlags.IsWriteAccess(flags))
            {
                if (FuseFlags.IsReadAccess(flags))
                {
                    openFlags = StorageLib.Abstractions.OpenFlags.ReadWrite;
                }
                else
                {
                    openFlags = StorageLib.Abstractions.OpenFlags.WriteOnly;
                }
            }

            // Add other flags
            if (FuseFlags.HasFlag(flags, FuseFlags.O_CREAT))
                openFlags |= StorageLib.Abstractions.OpenFlags.Create;

            if (FuseFlags.HasFlag(flags, FuseFlags.O_EXCL))
                openFlags |= StorageLib.Abstractions.OpenFlags.Exclusive;

            if (FuseFlags.HasFlag(flags, FuseFlags.O_TRUNC))
                openFlags |= StorageLib.Abstractions.OpenFlags.Truncate;

            if (FuseFlags.HasFlag(flags, FuseFlags.O_APPEND))
                openFlags |= StorageLib.Abstractions.OpenFlags.Append;

            if (FuseFlags.HasFlag(flags, FuseFlags.O_DIRECTORY))
                openFlags |= StorageLib.Abstractions.OpenFlags.Directory;

            if (FuseFlags.HasFlag(flags, FuseFlags.O_CREATEPARENTS))
                openFlags |= StorageLib.Abstractions.OpenFlags.CreateParentDirectories;

            return openFlags;
        }

    }
} 