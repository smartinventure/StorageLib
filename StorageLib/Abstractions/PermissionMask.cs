namespace StorageLib.Abstractions
{
    /// <summary>
    /// POSIX file permission constants and utilities for chmod operations.
    /// These constants are used to specify and manipulate file permission bits.
    /// </summary>
    public static class PermissionMask
    {
        /// <summary>
        /// File type mask (S_IFMT) - 0170000 octal or 0xF000 hex
        /// Used to extract file type bits from mode
        /// </summary>
        public const int S_IFMT = 0xF000;
        
        /// <summary>
        /// Permission bits only (without file type) - 0777 octal
        /// Used to extract only permission bits from mode
        /// </summary>
        public const int PERMISSION_MASK = 0x0FFF;
        
        // User (owner) permissions
        /// <summary>
        /// User read permission - 0400 octal
        /// </summary>
        public const int S_IRUSR = 0x0100;
        
        /// <summary>
        /// User write permission - 0200 octal
        /// </summary>
        public const int S_IWUSR = 0x0080;
        
        /// <summary>
        /// User execute permission - 0100 octal
        /// </summary>
        public const int S_IXUSR = 0x0040;
        
        /// <summary>
        /// All user permissions (read + write + execute) - 0700 octal
        /// </summary>
        public const int S_IRWXU = S_IRUSR | S_IWUSR | S_IXUSR;
        
        // Group permissions
        /// <summary>
        /// Group read permission - 0040 octal
        /// </summary>
        public const int S_IRGRP = 0x0020;
        
        /// <summary>
        /// Group write permission - 0020 octal
        /// </summary>
        public const int S_IWGRP = 0x0010;
        
        /// <summary>
        /// Group execute permission - 0010 octal
        /// </summary>
        public const int S_IXGRP = 0x0008;
        
        /// <summary>
        /// All group permissions (read + write + execute) - 0070 octal
        /// </summary>
        public const int S_IRWXG = S_IRGRP | S_IWGRP | S_IXGRP;
        
        // Other permissions
        /// <summary>
        /// Other read permission - 0004 octal
        /// </summary>
        public const int S_IROTH = 0x0004;
        
        /// <summary>
        /// Other write permission - 0002 octal
        /// </summary>
        public const int S_IWOTH = 0x0002;
        
        /// <summary>
        /// Other execute permission - 0001 octal
        /// </summary>
        public const int S_IXOTH = 0x0001;
        
        /// <summary>
        /// All other permissions (read + write + execute) - 0007 octal
        /// </summary>
        public const int S_IRWXO = S_IROTH | S_IWOTH | S_IXOTH;
        
        // Special permissions
        /// <summary>
        /// Set user ID on execution - 04000 octal
        /// </summary>
        public const int S_ISUID = 0x0800;
        
        /// <summary>
        /// Set group ID on execution - 02000 octal
        /// </summary>
        public const int S_ISGID = 0x0400;
        
        /// <summary>
        /// Sticky bit - 01000 octal
        /// </summary>
        public const int S_ISVTX = 0x0200;
        
        /// <summary>
        /// Extracts only the permission bits from a mode value, stripping file type bits.
        /// This is used to implement the CBFS documentation requirement that file type bits
        /// (S_IFMT) may be missing from the mode parameter in chmod operations.
        /// </summary>
        /// <param name="mode">The full mode value including potential file type bits</param>
        /// <returns>Only the permission bits (0-0777)</returns>
        public static int GetPermissionBits(int mode)
        {
            return mode & PERMISSION_MASK;
        }
        
        /// <summary>
        /// Checks if a mode has specific permission bits set.
        /// </summary>
        /// <param name="mode">The mode to check</param>
        /// <param name="permissionBits">The permission bits to check for</param>
        /// <returns>True if all specified permission bits are set</returns>
        public static bool HasPermission(int mode, int permissionBits)
        {
            int actualPermissions = GetPermissionBits(mode);
            return (actualPermissions & permissionBits) == permissionBits;
        }
        
        /// <summary>
        /// Checks if the mode indicates a read-only file (no write permissions for user).
        /// </summary>
        /// <param name="mode">The mode to check</param>
        /// <returns>True if the file is read-only (user write bit not set)</returns>
        public static bool IsReadOnly(int mode)
        {
            return !HasPermission(mode, S_IWUSR);
        }
        
        /// <summary>
        /// Creates a mode with specific permission bits set.
        /// </summary>
        /// <param name="permissionBits">The permission bits to set</param>
        /// <returns>A mode value with only the specified permission bits</returns>
        public static int CreateMode(int permissionBits)
        {
            return GetPermissionBits(permissionBits);
        }
        
        /// <summary>
        /// Converts permission bits to a human-readable string (e.g., "rwxr-xr-x").
        /// </summary>
        /// <param name="mode">The mode to convert</param>
        /// <returns>A string representation of the permissions</returns>
        public static string ToPermissionString(int mode)
        {
            int permissions = GetPermissionBits(mode);
            
            char[] result = new char[9];
            
            // User permissions
            result[0] = (permissions & S_IRUSR) != 0 ? 'r' : '-';
            result[1] = (permissions & S_IWUSR) != 0 ? 'w' : '-';
            result[2] = (permissions & S_IXUSR) != 0 ? 'x' : '-';
            
            // Group permissions
            result[3] = (permissions & S_IRGRP) != 0 ? 'r' : '-';
            result[4] = (permissions & S_IWGRP) != 0 ? 'w' : '-';
            result[5] = (permissions & S_IXGRP) != 0 ? 'x' : '-';
            
            // Other permissions
            result[6] = (permissions & S_IROTH) != 0 ? 'r' : '-';
            result[7] = (permissions & S_IWOTH) != 0 ? 'w' : '-';
            result[8] = (permissions & S_IXOTH) != 0 ? 'x' : '-';
            
            return new string(result);
        }
    }
} 