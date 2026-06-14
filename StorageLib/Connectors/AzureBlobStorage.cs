/*using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FolderMagicLib.Model;
using SlideMover.Model;

namespace StorageLib.Connectors
{
    internal class AzureBlobStorage : IStorage
    {
        private BlobClient _blobClient;
        private string _container;
        private BlobContainerClient _containerClient;

        public EnumStorageType Type { get => EnumStorageType.AzureBlob; set => throw new NotImplementedException(); }
        public string BaseFolderOrContainer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string ConnectionString { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Initialize(string connectionString, string baseFolderOrContainer)
        {
            // For Azure Blob Storage, the connection string can include the container and blob name
            // DefaultEndpointsProtocol = https; AccountName = your_account_name; AccountKey = your_account_key; EndpointSuffix = core.windows.net
            // BlobEndpoint=https://your_account_name.blob.core.windows.net/;SharedAccessSignature=your_sas_token

            _container = baseFolderOrContainer;
            BlobServiceClient blobServiceClient = new BlobServiceClient(connectionString);
            _containerClient = blobServiceClient.GetBlobContainerClient(_container);
        }

        public EnumStorageType GetStorageType()
        {
            return EnumStorageType.AzureBlob;
        }

        public Stream GetReadStream(string blobName)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            var stream = new MemoryStream();
            blobClient.DownloadTo(stream);
            stream.Position = 0; // Reset stream position after download
            return stream;
        }

        public Stream GetWriteStream(string blobName)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            var stream = new MemoryStream();
            return new BlobWriteStream(blobClient, stream);
        }

        public IEnumerable<string> GetFiles(string directoryPath, string searchPattern, SearchOption searchOption)
        {
            var blobs = _containerClient.GetBlobs(prefix: directoryPath).AsPages().SelectMany(page => page.Values);
            var regex = new System.Text.RegularExpressions.Regex(WildcardToRegex(searchPattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return searchOption == SearchOption.AllDirectories
                ? blobs.Where(blob => regex.IsMatch(blob.Name)).Select(blob => blob.Name)
                : blobs.Where(blob => regex.IsMatch(blob.Name) && Path.GetDirectoryName(blob.Name) == directoryPath).Select(blob => blob.Name);
        }

        public IEnumerable<string> GetFiles(string directoryPath)
        {
            return GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);
        }

        public IEnumerable<string> GetDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
        {
            var blobs = _containerClient.GetBlobsByHierarchy(prefix: directoryPath, delimiter: "/")
                                       .Where(b => b.IsPrefix)
                                       .Select(b => b.Prefix);

            var regex = new System.Text.RegularExpressions.Regex(WildcardToRegex(searchPattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return searchOption == SearchOption.AllDirectories
                ? blobs.Where(prefix => regex.IsMatch(prefix))
                : blobs.Where(prefix => regex.IsMatch(prefix) && Path.GetDirectoryName(prefix) == directoryPath);
        }

        public bool TestRead()
        {
            //The path is the container. List a few files to try reading
            return _containerClient.Exists();
        }

        public bool TestWrite()
        {
            try
            {
                // Test writing a file to the container
                using var stream = new MemoryStream();
                stream.WriteByte(0);
                WriteFile("testfile.bin", stream);
                _blobClient.DeleteIfExists();
                return true;
            }
            catch
            {
                return false;
            }

        }

        public void ReadFile(string filePath, Stream destinationStream)
        {
            _blobClient = _containerClient.GetBlobClient(filePath);
            _blobClient.DownloadTo(destinationStream);
        }

        public void WriteFile(string filePath, Stream data)
        {
            data.Position = 0; // Ensure the stream position is at the beginning
            _blobClient = _containerClient.GetBlobClient(filePath);
            _blobClient.Upload(data, true);
        }

        public bool FileExists(string blobName)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            return blobClient.Exists();
        }

        public bool DirectoryExists(string directoryPath)
        {
            // Azure Blob Storage does not have real directories
            return true;
        }

        public FileObject GetFileInfo(string blobName)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            BlobProperties properties = blobClient.GetProperties();
            return new FileObject(blobName, this)
            {
                RealPath = blobClient.Name,
                SC = this,
                Size = properties.ContentLength,
                CreationTime = properties.CreatedOn.DateTime,
                LastModified = properties.LastModified.DateTime,
                LastAccessTime = properties.LastAccessed.DateTime,
                Mode = 0, // Set appropriate mode
                Uid = 0, // Set appropriate UID
                Gid = 0  // Set appropriate GID
            };
        }


        private static string WildcardToRegex(string pattern)
        {
            return "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        }

        public void Delete(string blobName)
        {
            var blobClient = _containerClient.GetBlobClient(blobName);
            blobClient.DeleteIfExists();
        }

        public void Move(string sourceBlobName, string destinationBlobName, bool overwrite)
        {
            var sourceBlobClient = _containerClient.GetBlobClient(sourceBlobName);
            var destinationBlobClient = _containerClient.GetBlobClient(destinationBlobName);

            if (overwrite && destinationBlobClient.Exists())
            {
                destinationBlobClient.Delete();
            }

            destinationBlobClient.StartCopyFromUri(sourceBlobClient.Uri);
            sourceBlobClient.Delete();
        }

        public void CreateDirectory(string directoryPath)
        {
            // Azure Blob Storage does not require explicit directory creation.
            return;
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            var blobs = _containerClient.GetBlobsByHierarchy(prefix: path, delimiter: "/").AsPages().SelectMany(page => page.Values);
            return blobs.Select(blob => blob.IsPrefix ? blob.Prefix : blob.Blob.Name);
        }

        public void DeleteDirectory(string path)
        {
            var blobs = _containerClient.GetBlobsByHierarchy(prefix: path).AsPages().SelectMany(page => page.Values);
            foreach (var blob in blobs)
            {
                var blobClient = _containerClient.GetBlobClient(blob.IsPrefix ? blob.Prefix : blob.Blob.Name);
                blobClient.DeleteIfExists();
            }
        }


        public void Close()
        {
            // No closing needed for Azure Blob Storage
        }

        public void Dispose()
        {
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