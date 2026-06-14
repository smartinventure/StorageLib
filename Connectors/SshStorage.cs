/*
using FolderMagicLib.Model;
using Renci.SshNet;
using SlideMover.Model;
using System.Web;

namespace StorageLib.Connectors
{
    


public class SshStorage : IStorage
{
    private SftpClient _sftpClient;
    private string _baseFolder;

    public EnumStorageType Type { get => EnumStorageType.SSH; set => throw new NotImplementedException(); }
    public string BaseFolderOrContainer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    public string ConnectionString { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

    public void Initialize(string connectionString, string baseFolderOrContainer)
    {
        _baseFolder = baseFolderOrContainer;
        var uri = new Uri(connectionString);
        var username = uri.UserInfo.Split(':')[0];
        var password = uri.UserInfo.Contains(':') ? uri.UserInfo.Split(':')[1] : null;
        var host = uri.Host;
        var port = uri.Port > 0 ? uri.Port : 22; // Default to port 22 if not specified

        var queryParams = HttpUtility.ParseQueryString(uri.Query);
        var keyFilePath = queryParams["keyfile"];
        var keyFilePassphrase = queryParams["passphrase"];

        if (!string.IsNullOrEmpty(keyFilePath))
        {
            // Keyfile-based authentication
            if (!string.IsNullOrEmpty(keyFilePassphrase))
            {
                var keyFile = new PrivateKeyFile(keyFilePath, keyFilePassphrase);
                _sftpClient = new SftpClient(host, port, username, keyFile);
            }
            else
            {
                var keyFile = new PrivateKeyFile(keyFilePath);
                _sftpClient = new SftpClient(host, port, username, keyFile);
            }
        }
        else if (!string.IsNullOrEmpty(password))
        {
            // Password-based authentication
            _sftpClient = new SftpClient(host, port, username, password);
        }
        else
        {
            throw new ArgumentException("Either password or keyfile must be provided in the connection string.");
        }

        _sftpClient.Connect();
    }

    public EnumStorageType GetStorageType()
    {
        return EnumStorageType.SSH;
    }

    public Stream GetReadStream(string filePath)
    {
        return _sftpClient.OpenRead(filePath);
    }

    public Stream GetWriteStream(string filePath)
    {
        return _sftpClient.OpenWrite(filePath);
    }

    public IEnumerable<string> GetFiles(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        var regex = new System.Text.RegularExpressions.Regex(WildcardToRegex(searchPattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (searchOption == SearchOption.AllDirectories)
        {
            return GetFilesRecursive(directoryPath, regex);
        }
        else
        {
            return _sftpClient.ListDirectory(directoryPath)
                              .Where(f => f.IsRegularFile && regex.IsMatch(f.Name))
                              .Select(f => f.FullName);
        }
    }

    public IEnumerable<string> GetFiles(string directoryPath)
    {
        return GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);
    }

    public IEnumerable<string> GetDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
    {
        var directories = _sftpClient.ListDirectory(directoryPath)
                                    .Where(d => d.IsDirectory && d.Name != "." && d.Name != "..");

        var regex = new System.Text.RegularExpressions.Regex(WildcardToRegex(searchPattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        if (searchOption == SearchOption.AllDirectories)
        {
            return GetDirectoriesRecursive(directoryPath, regex);
        }
        else
        {
            return directories.Where(d => regex.IsMatch(d.Name))
                              .Select(d => d.FullName);
        }
    }


    public bool TestRead()
    {
        try
        {
            return _sftpClient.Exists(_baseFolder);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool TestWrite()
    {
        try
        {
            var testfile = Path.Combine(_baseFolder, "testfile.bin");
            using (var fileStream = new MemoryStream())
            {
                fileStream.WriteByte(0);
                WriteFile(testfile, fileStream);
            }
            _sftpClient.DeleteFile(testfile);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void ReadFile(string filePath, Stream destinationStream)
    {
        _sftpClient.DownloadFile(filePath, destinationStream);
        destinationStream.Position = 0; // Reset stream position after download
    }

    public void WriteFile(string filePath, Stream data)
    {
        data.Position = 0; // Ensure the stream position is at the beginning
        _sftpClient.UploadFile(data, filePath);
    }

    public bool FileExists(string filePath)
    {
        return _sftpClient.Exists(filePath);
    }

    public bool DirectoryExists(string directoryPath)
    {
        return _sftpClient.Exists(directoryPath) && _sftpClient.GetAttributes(directoryPath).IsDirectory;
    }

    public FileObject GetFileInfo(string filePath)
    {
        var attributes = _sftpClient.GetAttributes(filePath);
        return new FileObject(filePath, this)
        {
            RealPath = filePath,
            SC = this,
            Size = attributes.Size,
            CreationTime = attributes.LastWriteTime,
            LastModified = attributes.LastWriteTime,
            LastAccessTime = attributes.LastAccessTime,
            Mode = attributes.GetBytes()[0], // Assuming mode is the first byte
            Uid = attributes.UserId,
            Gid = attributes.GroupId
        };
    }

    private static string WildcardToRegex(string pattern)
    {
        return "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
    }

    private IEnumerable<string> GetFilesRecursive(string directoryPath, System.Text.RegularExpressions.Regex regex)
    {
        var files = _sftpClient.ListDirectory(directoryPath)
                              .Where(f => f.IsRegularFile && regex.IsMatch(f.Name))
                              .Select(f => f.FullName);

        var directories = _sftpClient.ListDirectory(directoryPath)
                                    .Where(d => d.IsDirectory && d.Name != "." && d.Name != "..");

        foreach (var directory in directories)
        {
            files = files.Concat(GetFilesRecursive(directory.FullName, regex));
        }

        return files;
    }

    private IEnumerable<string> GetDirectoriesRecursive(string directoryPath, System.Text.RegularExpressions.Regex regex)
    {
        var directories = _sftpClient.ListDirectory(directoryPath)
                                    .Where(d => d.IsDirectory && d.Name != "." && d.Name != "..");

        var matchedDirectories = directories.Where(d => regex.IsMatch(d.Name))
                                            .Select(d => d.FullName);

        foreach (var directory in directories)
        {
            matchedDirectories = matchedDirectories.Concat(GetDirectoriesRecursive(directory.FullName, regex));
        }

        return matchedDirectories;
    }

    public void Delete(string filePath)
    {
        if (_sftpClient.Exists(filePath))
        {
            _sftpClient.DeleteFile(filePath);
        }
    }

    public void Move(string sourceFilePath, string destinationFilePath, bool overwrite)
    {
        if (overwrite && _sftpClient.Exists(destinationFilePath))
        {
            _sftpClient.DeleteFile(destinationFilePath);
        }

        _sftpClient.RenameFile(sourceFilePath, destinationFilePath);
    }

    public void CreateDirectory(string directoryPath)
    {
        if (!_sftpClient.Exists(directoryPath))
        {
            _sftpClient.CreateDirectory(directoryPath);
        }
    }

    public IEnumerable<string> EnumerateFileSystemEntries(string path)
    {
        return _sftpClient.ListDirectory(path).Select(entry => entry.FullName);
    }

    public void DeleteDirectory(string path)
    {
        var files = _sftpClient.ListDirectory(path);
        foreach (var file in files)
        {
            if (file.IsDirectory && file.Name != "." && file.Name != "..")
            {
                DeleteDirectory(file.FullName);
            }
            else if (file.IsRegularFile)
            {
                _sftpClient.DeleteFile(file.FullName);
            }
        }
        _sftpClient.DeleteDirectory(path);
    }



    public void Close()
    {
        _sftpClient?.Disconnect();
    }

    public void Dispose()
    {
        _sftpClient?.Dispose();
    }

    FileObject IStorage.GetFileInfo(string filePath)
    {
        throw new NotImplementedException();
    }

    public IEnumerable<string> EnumerateFilesAndDirectories(string path, bool readOnly = true)
    {
        throw new NotImplementedException();
    }

    IEnumerable<FileObject> IStorage.EnumerateFilesAndDirectories(string path, bool readOnly)
    {
        throw new NotImplementedException();
    }
}


}
*/