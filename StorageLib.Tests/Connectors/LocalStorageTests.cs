using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Logging;
using Moq;
using StorageLib.Connectors;
using StorageLib.Abstractions;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace StorageLib.Tests.Connectors
{
    [TestClass]
    public class LocalStorageTests
    {
        private string _testBaseDir = string.Empty;
        private LocalStorage _storage = null!;
        private Mock<ILogger<LocalStorage>> _mockLogger = null!;

        [TestInitialize]
        public void Setup()
        {
            // Create a unique temporary directory for this test run
            _testBaseDir = Path.Combine(Path.GetTempPath(), "LocalStorageTests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_testBaseDir); // Ensure the base itself exists

            _mockLogger = new Mock<ILogger<LocalStorage>>();

            _storage = new LocalStorage(_mockLogger.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Dispose the storage instance
            _storage?.Dispose();

            // Clean up the temporary directory
            if (Directory.Exists(_testBaseDir))
            {
                try
                {
                    Directory.Delete(_testBaseDir, true);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Failed to cleanup test directory {_testBaseDir}: {ex.Message}");
                    // Log or handle cleanup failure if necessary, but don't fail the test run
                }
            }
        }

        [TestMethod]
        public void Constructor_SetsCorrectType()
        {
            // Act & Assert
            Assert.AreEqual(EnumStorageType.Local, _storage.StorageType);
            Assert.AreEqual(EnumStorageType.Local, _storage.Type);
        }

        [TestMethod]
        public async Task InitializeAsync_ValidPath_Succeeds()
        {
            // Act
            await _storage.InitializeAsync(string.Empty, _testBaseDir);

            // Assert
            Assert.AreEqual(_testBaseDir, _storage.BaseFolderOrContainer);
            Assert.IsTrue(Directory.Exists(_testBaseDir));
        }

        [TestMethod]
        public async Task InitializeAsync_InvalidPath_ThrowsDirectoryNotFoundException()
        {
            // Arrange
            string invalidPath = Path.Combine(_testBaseDir, "non_existent_subdir");

            // Act & Assert
            await Assert.ThrowsExceptionAsync<DirectoryNotFoundException>(async () =>
            {
                await _storage.InitializeAsync(string.Empty, invalidPath);
            });
        }

        [TestMethod]
        public async Task InitializeAsync_EmptyPath_ThrowsArgumentException()
        {
            // Act & Assert
            await Assert.ThrowsExceptionAsync<ArgumentException>(async () =>
            {
                await _storage.InitializeAsync(string.Empty, string.Empty);
            });
        }

        [TestMethod]
        public async Task CreateDirectoryAsync_NewDirectory_CreatesSuccessfully()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string dirName = "NewFolder";
            string dirPath = "/" + dirName;

            // Act
            await _storage.CreateDirectoryAsync(dirPath);

            // Assert
            bool exists = await _storage.DirectoryExistsAsync(dirPath);
            Assert.IsTrue(exists);
            
            // Verify the directory exists on the file system
            string realPath = Path.Combine(_testBaseDir, dirName);
            Assert.IsTrue(Directory.Exists(realPath));
        }

        [TestMethod]
        public async Task CreateDirectoryAsync_ExistingDirectory_DoesNotThrow()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string dirName = "ExistingFolder";
            string dirPath = "/" + dirName;
            
            // Pre-create the directory
            string realPath = Path.Combine(_testBaseDir, dirName);
            Directory.CreateDirectory(realPath);

            // Act & Assert - should not throw
            await _storage.CreateDirectoryAsync(dirPath);
            Assert.IsTrue(Directory.Exists(realPath));
        }

        [TestMethod]
        public async Task DirectoryExistsAsync_ExistingDirectory_ReturnsTrue()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string dirName = "ExistingDirExists";
            string dirPath = "/" + dirName;
            string realPath = Path.Combine(_testBaseDir, dirName);
            Directory.CreateDirectory(realPath);

            // Act
            bool exists = await _storage.DirectoryExistsAsync(dirPath);

            // Assert
            Assert.IsTrue(exists);
        }

        [TestMethod]
        public async Task DirectoryExistsAsync_NonExistentDirectory_ReturnsFalse()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string dirName = "NonExistentDirExists";
            string dirPath = "/" + dirName;

            // Act
            bool exists = await _storage.DirectoryExistsAsync(dirPath);

            // Assert
            Assert.IsFalse(exists);
        }

        [TestMethod]
        public async Task DeleteDirectoryAsync_ExistingEmptyDirectory_DeletesSuccessfully()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string dirName = "ToDeleteEmpty";
            string dirPath = "/" + dirName;
            await _storage.CreateDirectoryAsync(dirPath);
            Assert.IsTrue(await _storage.DirectoryExistsAsync(dirPath)); // Verify creation

            // Act
            await _storage.DeleteDirectoryAsync(dirPath);

            // Assert
            bool exists = await _storage.DirectoryExistsAsync(dirPath);
            Assert.IsFalse(exists);
        }

        [TestMethod]
        public async Task DeleteDirectoryAsync_ExistingNonEmptyDirectory_DeletesRecursively()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string dirName = "ToDeleteNonEmpty";
            string dirPath = "/" + dirName;
            await _storage.CreateDirectoryAsync(dirPath);
            
            // Create a file in the directory
            string filePath = dirPath + "/test.txt";
            IntPtr handle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(filePath, handle);
            
            Assert.IsTrue(await _storage.DirectoryExistsAsync(dirPath)); // Verify creation
            Assert.IsTrue(await _storage.FileExistsAsync(filePath)); // Verify file creation

            // Act
            await _storage.DeleteDirectoryAsync(dirPath);

            // Assert
            bool exists = await _storage.DirectoryExistsAsync(dirPath);
            Assert.IsFalse(exists);
        }

        [TestMethod]
        public async Task DeleteDirectoryAsync_NonExistentDirectory_DoesNotThrow()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string dirName = "ToDeleteNonExistent";
            string dirPath = "/" + dirName;

            // Act & Assert - should not throw
            await _storage.DeleteDirectoryAsync(dirPath);
            Assert.IsFalse(await _storage.DirectoryExistsAsync(dirPath));
        }

        [TestMethod]
        public async Task FileExistsAsync_ExistingFile_ReturnsTrue()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string fileName = "existing_file.txt";
            string filePath = "/" + fileName;
            
            // Create the file
            IntPtr handle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(filePath, handle);

            // Act
            bool exists = await _storage.FileExistsAsync(filePath);

            // Assert
            Assert.IsTrue(exists);
        }

        [TestMethod]
        public async Task FileExistsAsync_NonExistentFile_ReturnsFalse()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string fileName = "non_existent_file.txt";
            string filePath = "/" + fileName;

            // Act
            bool exists = await _storage.FileExistsAsync(filePath);

            // Assert
            Assert.IsFalse(exists);
        }

        [TestMethod]
        public async Task OpenAsync_CreateNewFile_Success()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string fileName = "newfile.txt";
            string filePath = "/" + fileName;

            // Act
            IntPtr handle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.WriteOnly);

            // Assert
            Assert.AreNotEqual(IntPtr.Zero, handle);
            Assert.IsTrue(await _storage.FileExistsAsync(filePath));

            // Cleanup
            await _storage.CloseAsync(filePath, handle);
        }

        [TestMethod]
        public async Task OpenAsync_ReadNonExistentFile_ThrowsFileNotFoundException()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string fileName = "nonexistent.txt";
            string filePath = "/" + fileName;

            // Act & Assert
            await Assert.ThrowsExceptionAsync<FileNotFoundException>(async () =>
            {
                await _storage.OpenAsync(filePath, OpenFlags.ReadOnly);
            });
        }

        [TestMethod]
        public async Task TestReadAsync_ReturnsTrue()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);

            // Act
            bool canRead = await _storage.TestReadAsync();

            // Assert
            Assert.IsTrue(canRead);
        }

        [TestMethod]
        public async Task TestWriteAsync_ReturnsTrue()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);

            // Act
            bool canWrite = await _storage.TestWriteAsync();

            // Assert
            Assert.IsTrue(canWrite);
        }

        [TestMethod]
        public async Task GetTotalCapacityAsync_ReturnsPositiveValue()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);

            // Act
            long capacity = await _storage.GetTotalCapacityAsync();

            // Assert
            Assert.IsTrue(capacity > 0);
        }

        [TestMethod]
        public async Task GetAvailableFreeSpaceAsync_ReturnsPositiveValue()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);

            // Act
            long freeSpace = await _storage.GetAvailableFreeSpaceAsync();

            // Assert
            Assert.IsTrue(freeSpace > 0);
        }

        [TestMethod]
        public async Task ReadWriteFile_BasicOperations_WorkCorrectly()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string filePath = "/test.txt";
            string testContent = "Hello, LocalStorage!";
            byte[] testData = System.Text.Encoding.UTF8.GetBytes(testContent);

            // Act - Write
            IntPtr writeHandle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.WriteOnly);
            
            IntPtr bufferPtr = System.Runtime.InteropServices.Marshal.AllocHGlobal(testData.Length);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(testData, 0, bufferPtr, testData.Length);
                await _storage.WriteAsync(filePath, writeHandle, 0, testData.Length, bufferPtr);
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(bufferPtr);
            }
            await _storage.CloseAsync(filePath, writeHandle);

            // Act - Read
            byte[] result = await _storage.ReadFileAsync(filePath);

            // Assert
            string resultContent = System.Text.Encoding.UTF8.GetString(result);
            Assert.AreEqual(testContent, resultContent);
        }

        [TestMethod]
        public async Task MoveAsync_RenameFile_Success()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string sourcePath = "/oldname.txt";
            string destPath = "/newname.txt";

            // Create source file
            IntPtr handle = await _storage.OpenAsync(sourcePath, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(sourcePath, handle);

            // Act
            await _storage.MoveAsync(sourcePath, destPath, false);

            // Assert
            Assert.IsFalse(await _storage.FileExistsAsync(sourcePath));
            Assert.IsTrue(await _storage.FileExistsAsync(destPath));
        }

        [TestMethod]
        public async Task DeleteAsync_RemoveFile_Success()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string filePath = "/todelete.txt";
            
            // Create file
            IntPtr handle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(filePath, handle);
            Assert.IsTrue(await _storage.FileExistsAsync(filePath));

            // Act
            await _storage.DeleteAsync(filePath);

            // Assert
            Assert.IsFalse(await _storage.FileExistsAsync(filePath));
        }

        [TestMethod]
        public async Task CloseAllHandlesAsync_ClosesAllOpenHandles()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            
            IntPtr handle1 = await _storage.OpenAsync("/file1.txt", OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr handle2 = await _storage.OpenAsync("/file2.txt", OpenFlags.Create | OpenFlags.WriteOnly);

            // Act
            await _storage.CloseAllHandlesAsync();

            // Assert - Verify we can still access files (handles were closed properly)
            bool file1Exists = await _storage.FileExistsAsync("/file1.txt");
            bool file2Exists = await _storage.FileExistsAsync("/file2.txt");
            Assert.IsTrue(file1Exists);
            Assert.IsTrue(file2Exists);
        }

        [TestMethod]
        public async Task GetFileInfoAsync_ExistingFile_ReturnsCorrectInfo()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string filePath = "/info_test.txt";
            
            // Create file
            IntPtr handle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(filePath, handle);

            // Act
            var fileInfo = await _storage.GetFileInfoAsync(filePath);

            // Assert
            Assert.IsNotNull(fileInfo);
            Assert.IsFalse(fileInfo.IsDirectory);
            Assert.AreEqual("info_test.txt", fileInfo.Filename);
            Assert.AreEqual(filePath, fileInfo.RealPath);
        }

        [TestMethod]
        public async Task SetTimesAsync_UpdatesFileTimes()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string filePath = "/time_test.txt";
            
            // Create file
            IntPtr handle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(filePath, handle);

            DateTime newAccessTime = new DateTime(2023, 1, 1, 12, 0, 0);
            DateTime newModifiedTime = new DateTime(2023, 1, 2, 12, 0, 0);

            // Act
            await _storage.SetTimesAsync(filePath, newAccessTime, newModifiedTime);

            // Assert
            var fileInfo = await _storage.GetFileInfoAsync(filePath);
            Assert.AreEqual(newAccessTime, fileInfo.LastAccessTime);
            Assert.AreEqual(newModifiedTime, fileInfo.LastModified);
        }

        [TestMethod]
        public async Task SetTimesAsync_FileOpenWithUnwrittenData_TimesSurviveTheClose()
        {
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            string filePath = "/open_time_test.txt";
            byte[] data = Encoding.UTF8.GetBytes("written before the times are set");
            DateTime newAccessTime = new DateTime(2023, 1, 1, 12, 0, 0);
            DateTime newModifiedTime = new DateTime(2023, 1, 2, 12, 0, 0);

            IntPtr handle = await _storage.OpenAsync(filePath, OpenFlags.Create | OpenFlags.ReadWrite);
            IntPtr buffer = Marshal.AllocHGlobal(data.Length);
            try
            {
                Marshal.Copy(data, 0, buffer, data.Length);
                await _storage.WriteAsync(filePath, handle, 0, data.Length, buffer);
                await _storage.SetTimesAsync(filePath, newAccessTime, newModifiedTime);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
            await _storage.CloseAsync(filePath, handle);

            var fileInfo = await _storage.GetFileInfoAsync(filePath);
            Assert.AreEqual(newModifiedTime, fileInfo.LastModified);
            Assert.AreEqual(newAccessTime, fileInfo.LastAccessTime);
            Assert.AreEqual(data.Length, fileInfo.Size);
        }

        [TestMethod]
        public async Task GetFileInfoAsync_Directory_ReturnsDirectory()
        {
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            await _storage.CreateDirectoryAsync("/info_dir");

            var fileInfo = await _storage.GetFileInfoAsync("/info_dir");

            Assert.IsTrue(fileInfo.IsDirectory);
            Assert.AreEqual("info_dir", fileInfo.Filename);
            Assert.AreEqual(0, fileInfo.Size);
        }

        [TestMethod]
        public async Task GetFileInfoAsync_MissingPath_ThrowsFileNotFoundException()
        {
            await _storage.InitializeAsync(string.Empty, _testBaseDir);

            await Assert.ThrowsExactlyAsync<FileNotFoundException>(() => _storage.GetFileInfoAsync("/missing.txt"));
        }

        [TestMethod]
        public async Task EnumerateFilesAndDirectoriesAsync_ReturnsCorrectEntries()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            
            // Create a directory and file
            await _storage.CreateDirectoryAsync("/testdir");
            IntPtr handle = await _storage.OpenAsync("/testfile.txt", OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync("/testfile.txt", handle);

            // Act
            var entries = await _storage.ReadDirAsync("/");

            // Assert
            Assert.AreEqual(2, entries.Count());
            
            var fileEntry = entries.FirstOrDefault(e => e.Filename == "testfile.txt");
            var dirEntry = entries.FirstOrDefault(e => e.Filename == "testdir");
            
            Assert.IsNotNull(fileEntry);
            Assert.IsNotNull(dirEntry);
            Assert.IsFalse(fileEntry.IsDirectory);
            Assert.IsTrue(dirEntry.IsDirectory);
        }

        [TestMethod]
        public async Task Dispose_CleansUpResources()
        {
            // Arrange
            await _storage.InitializeAsync(string.Empty, _testBaseDir);
            IntPtr handle = await _storage.OpenAsync("/test.txt", OpenFlags.Create | OpenFlags.WriteOnly);

            // Act
            _storage.Dispose();

            // Assert - Should not throw (proper cleanup)
            Assert.IsTrue(true); // If we get here, disposal worked
        }
    }
} 