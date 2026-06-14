# StorageLib

Universal storage abstraction library for .NET applications supporting local, cloud, and remote storage backends.

## Features

- **Universal Interface**: Single `IStorage` interface for all storage types
- **Multiple Backends**: Local filesystem, memory, Azure Blob, AWS S3, SSH/SFTP
- **Throttling Support**: Built-in read/write speed limiting
- **Async/Await**: Fully asynchronous operations with cancellation support
- **Extensible**: Easy to add new storage backends

## Quick Start

```csharp
using StorageLib.Abstractions;
using StorageLib.Connectors;

// Local storage
var localStorage = new LocalStorage();
await localStorage.InitializeAsync("", @"C:\data");

// List files
var files = await localStorage.GetFilesAsync("/documents");

// Read file info
var fileInfo = await localStorage.GetFileInfoAsync("/documents/example.txt");
```

## Supported Storage Types

- **Local**: Local filesystem storage
- **Memory**: In-memory storage (volatile)
- **Azure**: Azure Blob Storage
- **AWS**: Amazon S3 storage
- **SSH**: SSH/SFTP storage
- **OpenSlide**: Virtual whole-slide imaging system

## License

MIT License 