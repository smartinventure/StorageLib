namespace StorageLib.Abstractions;

/// <summary>
/// A layer that may report a write as saved before the backend has it, and can say what is still
/// outstanding. Implemented by anything that uploads in the background, so a mount can tell the user
/// what would be lost by pulling the plug and can wait for it on unmount.
/// </summary>
public interface IPendingUploadSource
{
    /// <summary>Name shown to the user for whatever is holding the files back.</summary>
    string PendingUploadSourceName { get; }

    /// <summary>
    /// Reads what is still waiting to be uploaded. Returns <see cref="PendingUploadState.None"/> when
    /// the source cannot be reached rather than throwing: this is reporting, and it must not be able to
    /// break the caller.
    /// </summary>
    Task<PendingUploadState> GetPendingUploadsAsync(CancellationToken cancellationToken = default);
}
