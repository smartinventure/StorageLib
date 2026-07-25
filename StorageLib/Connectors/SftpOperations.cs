using Renci.SshNet;
using Renci.SshNet.Sftp;
using StorageLib.Abstractions;

namespace StorageLib.Connectors;

internal static class SftpOperations
{
    public static void CreateDirectoryPath(SftpClient client, string path)
    {
        string current = "/";
        foreach (string segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            current = current == "/" ? $"/{segment}" : $"{current}/{segment}";
            if (!client.Exists(current))
            {
                client.CreateDirectory(current);
            }
        }
    }

    public static void DeleteDirectoryRecursive(
        SftpClient client,
        string path,
        CancellationToken cancellationToken)
    {
        foreach (var entry in client.ListDirectory(path).Where(item => item.Name is not "." and not ".."))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry.IsDirectory)
            {
                DeleteDirectoryRecursive(client, entry.FullName, cancellationToken);
            }
            else
            {
                client.DeleteFile(entry.FullName);
            }
        }

        client.DeleteDirectory(path);
    }

    public static FileObject CreateFileObject(
        IStorage storage,
        string path,
        string name,
        SftpFileAttributes attributes,
        bool isReadOnly) =>
        new(path, storage)
        {
            Filename = name,
            RealPath = path,
            IsDirectory = attributes.IsDirectory,
            IsReadOnly = isReadOnly,
            Size = attributes.IsDirectory ? 0 : attributes.Size,
            LastAccessTime = attributes.LastAccessTime,
            LastModified = attributes.LastWriteTime,
            CreationTime = attributes.LastWriteTime,
            Uid = unchecked((int)attributes.UserId),
            Gid = unchecked((int)attributes.GroupId)
        };
}
