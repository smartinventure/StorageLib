namespace StorageLib.Abstractions;

/// <summary>
/// What is still on its way to a backend: files that have been written and reported as saved but whose
/// upload has not finished yet.
/// </summary>
/// <param name="FileCount">Files waiting to be uploaded or uploading right now.</param>
/// <param name="ByteCount">Bytes those files hold, or zero where the source cannot say.</param>
/// <param name="FailedFileCount">
/// Files whose upload has failed at least once and is being retried. They are also counted in
/// <paramref name="FileCount"/>; they matter separately because they are the ones that may need help.
/// </param>
public sealed record PendingUploadState(int FileCount, long ByteCount, int FailedFileCount)
{
    /// <summary>Nothing is waiting.</summary>
    public static PendingUploadState None { get; } = new(0, 0, 0);

    public bool HasPendingUploads => FileCount > 0;

    public PendingUploadState Add(PendingUploadState other) => new(
        FileCount + other.FileCount,
        ByteCount + other.ByteCount,
        FailedFileCount + other.FailedFileCount);
}
