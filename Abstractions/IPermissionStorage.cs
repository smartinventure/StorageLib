namespace StorageLib.Abstractions
{
    /// <summary>
    /// Interface for storage backends that support POSIX-style permission operations.
    /// Storage implementations can implement this to provide chmod and chown functionality.
    /// </summary>
    public interface IPermissionStorage
    {
        /// <summary>
        /// Changes the permission bits of a file or directory.
        /// </summary>
        /// <param name="path">The path to the file/directory</param>
        /// <param name="permissionBits">The new permission bits (0-0777)</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task ChmodAsync(string path, int permissionBits, CancellationToken cancellationToken = default);

        /// <summary>
        /// Changes the owner and group of a file or directory.
        /// </summary>
        /// <param name="path">The path to the file/directory</param>
        /// <param name="uid">The new user ID</param>
        /// <param name="gid">The new group ID</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the async operation</returns>
        Task ChownAsync(string path, int uid, int gid, CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if the storage backend supports permission operations.
        /// </summary>
        /// <returns>True if chmod/chown operations are supported</returns>
        bool SupportsPermissions { get; }
    }
} 