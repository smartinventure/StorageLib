/*
using Amazon.S3;
using Amazon.S3.Model;
using FolderMagicLib.Model;
using SlideMover.Model;

namespace StorageLib.Connectors
{

    public class S3Storage : IStorage
    {
        private AmazonS3Client _s3Client;

        private string _bucketName;

        public EnumStorageType Type { get => EnumStorageType.S3; set => throw new NotImplementedException(); }
        public string BaseFolderOrContainer { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public string ConnectionString { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        EnumStorageType IStorage.Type { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        public void Initialize(string connectionString, string baseFolderOrContainer)
        {
            // For AWS S3, the connection string can be a combination of AccessKey, SecretKey, and Region.
            // connectionString example: "AccessKey=your_access_key;SecretKey=your_secret_key;Region=us-west-1"

            var connectionStringParts = connectionString.Split(';');
            var accessKey = connectionStringParts.FirstOrDefault(p => p.StartsWith("AccessKey="))?.Split('=')[1];
            var secretKey = connectionStringParts.FirstOrDefault(p => p.StartsWith("SecretKey="))?.Split('=')[1];
            var region = connectionStringParts.FirstOrDefault(p => p.StartsWith("Region="))?.Split('=')[1];

            if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(region))
            {
                throw new ArgumentException("S3 requires AccessKey, SecretKey, and Region in the connection string.");
            }

            _bucketName = baseFolderOrContainer;

            var config = new AmazonS3Config
            {
                RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(region)
            };
            _s3Client = new AmazonS3Client(accessKey, secretKey, config);
        }



        public EnumStorageType GetStorageType()
        {
            return EnumStorageType.S3;
        }

        public IEnumerable<string> GetDirectories(string directoryPath, string searchPattern, SearchOption searchOption)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = directoryPath.EndsWith("/") ? directoryPath : directoryPath + "/",
                Delimiter = "/"
            };
            var response = _s3Client.ListObjectsV2Async(request).Result;

            var directories = response.CommonPrefixes;

            var regex = new System.Text.RegularExpressions.Regex(WildcardToRegex(searchPattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return searchOption == SearchOption.AllDirectories
                ? directories.Where(prefix => regex.IsMatch(prefix))
                : directories.Where(prefix => regex.IsMatch(prefix) && Path.GetDirectoryName(prefix.TrimEnd('/')) == Path.GetDirectoryName(directoryPath.TrimEnd('/')));
        }

        public bool TestRead()
        {
            try
            {
                // List a few files to try reading
                var request = new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    MaxKeys = 1
                };
                var response = _s3Client.ListObjectsV2Async(request).Result;
                return response.S3Objects.Any();
            }
            catch
            {
                return false;
            }
        }

        public bool TestWrite()
        {
            try
            {
                // Test writing a file to the bucket
                using var stream = new MemoryStream();
                stream.WriteByte(0);
                WriteFile("testfile.bin", stream);
                Delete("testfile.bin");
                return true;
            }
            catch
            {
                return false;
            }
        }

        public void ReadFile(string filePath, Stream destinationStream)
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = filePath
            };
            using var response = _s3Client.GetObjectAsync(request).Result;
            response.ResponseStream.CopyTo(destinationStream);
        }

        public void WriteFile(string filePath, Stream data)
        {
            data.Position = 0; // Ensure the stream position is at the beginning
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = filePath,
                InputStream = data
            };
            _s3Client.PutObjectAsync(request).Wait();
        }

        public Stream GetReadStream(string filePath)
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = filePath
            };
            using var response = _s3Client.GetObjectAsync(request).Result;
            return response.ResponseStream;
        }

        public Stream GetWriteStream(string filePath)
        {
            var stream = new MemoryStream();
            stream.Position = 0;
            _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = filePath,
                InputStream = stream
            }).Wait();
            return stream;
        }

        public bool FileExists(string filePath)
        {
            try
            {
                var request = new GetObjectMetadataRequest
                {
                    BucketName = _bucketName,
                    Key = filePath
                };
                _s3Client.GetObjectMetadataAsync(request).Wait();
                return true;
            }
            catch (AggregateException e) when (e.InnerException is AmazonS3Exception s3Ex && s3Ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
        }

        public bool DirectoryExists(string directoryPath)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = directoryPath.EndsWith("/") ? directoryPath : directoryPath + "/",
                MaxKeys = 1
            };
            var response = _s3Client.ListObjectsV2Async(request).Result;
            return response.S3Objects.Any();
        }

        public FileObject GetFileInfo(string filePath)
        {
            var request = new GetObjectMetadataRequest
            {
                BucketName = _bucketName,
                Key = filePath
            };
            var response = _s3Client.GetObjectMetadataAsync(request).Result;
            return new FileObject(filePath, this)
            {
                RealPath = filePath,
                SC = this,
                Size = response.ContentLength,
                LastModified = response.LastModified,
                CreationTime = DateTime.MinValue, // S3 does not provide creation time
                LastAccessTime = DateTime.MinValue, // S3 does not provide last access time
                Mode = 0, // S3 does not provide mode
                Uid = 0, // S3 does not provide UID
                Gid = 0 // S3 does not provide GID
            };
        }

        public IEnumerable<string> GetFiles(string directoryPath, string searchPattern, SearchOption searchOption)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = directoryPath,
                Delimiter = searchOption == SearchOption.TopDirectoryOnly ? "/" : null
            };
            var response = _s3Client.ListObjectsV2Async(request).Result;
            var regex = new System.Text.RegularExpressions.Regex(WildcardToRegex(searchPattern), System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            return response.S3Objects.Where(o => regex.IsMatch(o.Key)).Select(o => o.Key);
        }

        public IEnumerable<string> GetFiles(string directoryPath)
        {
            return GetFiles(directoryPath, "*", SearchOption.TopDirectoryOnly);
        }

        public long GetSpaceLeftOnTarget(string targetFilePath)
        {
            // S3 does not provide direct storage space querying. This method might require custom implementation.
            throw new NotImplementedException();
        }

        public void CreateDirectory(string directoryPath)
        {
            var key = directoryPath.EndsWith("/") ? directoryPath : directoryPath + "/";
            _s3Client.PutObjectAsync(new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                ContentBody = string.Empty // Create a zero-byte object
            }).Wait();
        }

        public void Delete(string filePath)
        {
            var request = new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = filePath
            };
            _s3Client.DeleteObjectAsync(request).Wait();
        }

        public void Move(string sourceFilePath, string destinationFilePath, bool overwrite)
        {
            if (overwrite && FileExists(destinationFilePath))
            {
                Delete(destinationFilePath);
            }

            var copyRequest = new CopyObjectRequest
            {
                SourceBucket = _bucketName,
                SourceKey = sourceFilePath,
                DestinationBucket = _bucketName,
                DestinationKey = destinationFilePath
            };
            _s3Client.CopyObjectAsync(copyRequest).Wait();
            Delete(sourceFilePath);
        }

        public IEnumerable<string> EnumerateFileSystemEntries(string path)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = path
            };
            var response = _s3Client.ListObjectsV2Async(request).Result;
            return response.S3Objects.Select(o => o.Key);
        }

        public void DeleteDirectory(string path)
        {
            var request = new ListObjectsV2Request
            {
                BucketName = _bucketName,
                Prefix = path
            };
            var response = _s3Client.ListObjectsV2Async(request).Result;
            foreach (var s3Object in response.S3Objects)
            {
                Delete(s3Object.Key);
            }
        }

        public void Close()
        {
            _s3Client.Dispose();
        }

        public void Dispose()
        {
            _s3Client.Dispose();
        }

        private static string WildcardToRegex(string pattern)
        {
            return "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$";
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