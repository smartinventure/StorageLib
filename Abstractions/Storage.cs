using System.Text.Json.Serialization;
using StorageLib.Abstractions;
using StorageLib.Connectors;

namespace StorageLib.Abstractions
{
    public class Storage : IDisposable
    {
        public EnumStorageType Type { get; set; }
        public string? ConnectionString { get; set; }
        public string? MountPoint { get; set; }
        public string? BaseFolderOrContainer { get; set; }

        [JsonIgnore]
        private StorageLib.Abstractions.IStorage? _storageConnector;

        public async Task<StorageLib.Abstractions.IStorage> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (_storageConnector != null)
            {
                _storageConnector.Dispose();
            }

            if (string.IsNullOrEmpty(ConnectionString) || string.IsNullOrEmpty(BaseFolderOrContainer))
            {
                throw new Exception("Storage connection string or base folder not set.");
            }

            switch (Type)
            {
                case EnumStorageType.Local:
                    _storageConnector = new LocalStorage() { BaseFolderOrContainer = BaseFolderOrContainer };
                    break;
                case EnumStorageType.Memory:
                    _storageConnector = new MemoryStorage();
                    break;
                /*case EnumStorageType.AzureBlob:
                    _storageConnector = new AzureBlobStorage();
                    break;
                case EnumStorageType.SSH:
                    _storageConnector = new SshStorage();
                    break;
                */
                default:
                    throw new Exception($"Storage type '{Type}' not supported.");
            }

            await _storageConnector.InitializeAsync(ConnectionString, BaseFolderOrContainer, cancellationToken);
            return _storageConnector;
        }

        public StorageLib.Abstractions.IStorage Connect()
        {
            return ConnectAsync().GetAwaiter().GetResult();
        }

        public void Dispose()
        {
            _storageConnector?.Dispose();
        }
    }
}
