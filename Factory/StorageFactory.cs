using System.Threading;
using System.Threading.Tasks;

using StorageLib.Abstractions;
using StorageLib.Connectors;

namespace StorageLib.Factory
{
    public class StorageFactory
    {
        public static StorageLib.Abstractions.IStorage GetStorageConnector(EnumStorageType storageType)
        {
            switch (storageType)
            {
                case EnumStorageType.Local:
                    return new StorageLib.Connectors.LocalStorage();
                case EnumStorageType.Memory:
                    return new MemoryStorage();
                case EnumStorageType.WindowsMetaMemory:
                    return new WindowsMetaMemoryStorage();
                //case EnumStorageType.AzureBlob:
                //return new AzureBlobStorage();
                //case EnumStorageType.SSH:
                //return new SshStorage();
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        public static Task<StorageLib.Abstractions.IStorage> GetStorageConnectorAsync(EnumStorageType storageType, CancellationToken cancellationToken = default)
        {
            // Since creation is synchronous, we can just wrap it in a Task
            return Task.FromResult(GetStorageConnector(storageType));
        }
    }
}
