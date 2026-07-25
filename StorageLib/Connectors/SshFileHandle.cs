namespace StorageLib.Connectors;

internal sealed class SshFileHandle : IDisposable
{
    public SshFileHandle(string path, Stream stream, bool isReadOnly)
    {
        Path = path;
        Stream = stream;
        IsReadOnly = isReadOnly;
    }

    public string Path { get; }

    public Stream Stream { get; }

    public bool IsReadOnly { get; }

    public SemaphoreSlim Lock { get; } = new(1, 1);

    public bool IsClosed { get; private set; }

    public void Dispose()
    {
        if (IsClosed)
        {
            return;
        }

        IsClosed = true;
        Stream.Dispose();
    }
}
