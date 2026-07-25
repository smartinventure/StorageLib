using System.Runtime.InteropServices;

namespace StorageLib.Connectors;

internal static class SshHandleOperations
{
    public static async Task<int> ReadAsync(
        SshFileHandle handle,
        long offset,
        long size,
        IntPtr buffer,
        CancellationToken cancellationToken)
    {
        ValidateBuffer(buffer, size);
        byte[] data = new byte[checked((int)size)];
        handle.Stream.Seek(offset, SeekOrigin.Begin);
        int bytesRead = await handle.Stream.ReadAsync(data, cancellationToken).ConfigureAwait(false);
        Marshal.Copy(data, 0, buffer, bytesRead);
        return bytesRead;
    }

    public static async Task<int> WriteAsync(
        SshFileHandle handle,
        long offset,
        long size,
        IntPtr buffer,
        CancellationToken cancellationToken)
    {
        ValidateBuffer(buffer, size);
        if (handle.IsReadOnly)
        {
            throw new UnauthorizedAccessException("The SFTP handle is read-only.");
        }

        byte[] data = new byte[checked((int)size)];
        Marshal.Copy(buffer, data, 0, data.Length);
        handle.Stream.Seek(offset, SeekOrigin.Begin);
        await handle.Stream.WriteAsync(data, cancellationToken).ConfigureAwait(false);
        return data.Length;
    }

    public static async Task<int> FlushAsync(
        SshFileHandle handle,
        CancellationToken cancellationToken)
    {
        await handle.Stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        return 0;
    }

    public static Task<int> AllocateAsync(
        SshFileHandle handle,
        int mode,
        long offset,
        long length)
    {
        if ((mode & 1) == 0)
        {
            handle.Stream.SetLength(Math.Max(handle.Stream.Length, checked(offset + length)));
        }

        return Task.FromResult(0);
    }

    public static Task<int> TruncateAsync(SshFileHandle handle, long size)
    {
        handle.Stream.SetLength(size);
        return Task.FromResult(0);
    }

    private static void ValidateBuffer(IntPtr buffer, long size)
    {
        if (buffer == IntPtr.Zero)
        {
            throw new ArgumentException("Buffer cannot be null.", nameof(buffer));
        }

        if (size < 0 || size > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(size));
        }
    }
}
