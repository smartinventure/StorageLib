namespace StorageLib.Abstractions
{
    /// <summary>
    /// POSIX access() system call constants for checking file permissions.
    /// These constants are used to specify what type of access to check for a file or directory.
    /// </summary>
    public static class AccessMask
    {
        /// <summary>
        /// Test for existence of file
        /// </summary>
        public const int F_OK = 0;
        
        /// <summary>
        /// Test for read permission
        /// </summary>
        public const int R_OK = 4;
        
        /// <summary>
        /// Test for write permission
        /// </summary>
        public const int W_OK = 2;
        
        /// <summary>
        /// Test for execute permission
        /// </summary>
        public const int X_OK = 1;

        /// <summary>
        /// Checks if the mask includes read access
        /// </summary>
        /// <param name="mask">The access mask to check</param>
        /// <returns>True if read access is requested</returns>
        public static bool HasReadAccess(int mask) => (mask & R_OK) != 0;

        /// <summary>
        /// Checks if the mask includes write access
        /// </summary>
        /// <param name="mask">The access mask to check</param>
        /// <returns>True if write access is requested</returns>
        public static bool HasWriteAccess(int mask) => (mask & W_OK) != 0;

        /// <summary>
        /// Checks if the mask includes execute access
        /// </summary>
        /// <param name="mask">The access mask to check</param>
        /// <returns>True if execute access is requested</returns>
        public static bool HasExecuteAccess(int mask) => (mask & X_OK) != 0;

        /// <summary>
        /// Checks if this is only an existence check (F_OK)
        /// </summary>
        /// <param name="mask">The access mask to check</param>
        /// <returns>True if only checking for file existence</returns>
        public static bool IsExistenceCheck(int mask) => mask == F_OK;

        /// <summary>
        /// Gets a human-readable description of the access mask
        /// </summary>
        /// <param name="mask">The access mask to describe</param>
        /// <returns>String description of the requested access</returns>
        public static string GetDescription(int mask)
        {
            if (IsExistenceCheck(mask))
                return "Existence check (F_OK)";

            var parts = new List<string>();
            if (HasReadAccess(mask)) parts.Add("Read");
            if (HasWriteAccess(mask)) parts.Add("Write");
            if (HasExecuteAccess(mask)) parts.Add("Execute");

            return parts.Count > 0 ? string.Join(", ", parts) : "Unknown access";
        }
    }
} 