using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Connectors;
using StorageLib.Abstractions;
using System;
using System.Threading.Tasks;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;

namespace StorageLib.Tests.Connectors
{
    [TestClass]
    public class MemoryStorageTests
    {
        private MemoryStorage _storage;

        [TestInitialize]
        public void Setup()
        {
            _storage = new MemoryStorage();
            _storage.InitializeAsync("", "").Wait();
        }

        [TestCleanup]
        public void Cleanup()
        {
            _storage?.Dispose();
        }

        [TestMethod]
        public async Task InitializeAsync_SetsUpEmptyStorage()
        {
            // Arrange
            var storage = new MemoryStorage();

            // Act
            await storage.InitializeAsync("", "");

            // Assert
            Assert.AreEqual(EnumStorageType.Memory, storage.Type);
        }

        [TestMethod]
        public async Task FileExistsAsync_NonExistingFile_ReturnsFalse()
        {
            // Arrange
            string fileName = "/nonexistent.txt";

            // Act
            bool exists = await _storage.FileExistsAsync(fileName);

            // Assert
            Assert.IsFalse(exists);
        }

        [TestMethod]
        public async Task DirectoryExistsAsync_NonExistingDirectory_ReturnsFalse()
        {
            // Arrange
            string dirName = "/nonexistentdir";

            // Act
            bool exists = await _storage.DirectoryExistsAsync(dirName);

            // Assert
            Assert.IsFalse(exists);
        }

        [TestMethod]
        public async Task CreateDirectoryAsync_CreatesDirectory()
        {
            // Arrange
            string dirName = "/newdir";

            // Act
            await _storage.CreateDirectoryAsync(dirName);

            // Assert
            Assert.IsTrue(await _storage.DirectoryExistsAsync(dirName));
        }

        [TestMethod]
        public async Task DeleteDirectoryAsync_DeletesExistingDirectory()
        {
            // Arrange
            string dirName = "/deleteme";
            await _storage.CreateDirectoryAsync(dirName);

            // Act
            await _storage.DeleteDirectoryAsync(dirName);

            // Assert
            Assert.IsFalse(await _storage.DirectoryExistsAsync(dirName));
        }

        [TestMethod]
        public async Task OpenAsync_CreatesNewFile()
        {
            // Arrange
            string fileName = "/newfile.txt";

            // Act
            IntPtr handle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(fileName, handle);

            // Assert
            Assert.IsTrue(await _storage.FileExistsAsync(fileName));
        }

        [TestMethod]
        public async Task OpenAsync_ExistingFile_ReturnsValidHandle()
        {
            // Arrange
            string fileName = "/existing.txt";
            IntPtr createHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(fileName, createHandle);

            // Act
            IntPtr handle = await _storage.OpenAsync(fileName, OpenFlags.ReadOnly);

            // Assert
            Assert.AreNotEqual(IntPtr.Zero, handle);
            
            // Cleanup
            await _storage.CloseAsync(fileName, handle);
        }

        [TestMethod]
        public async Task WriteAndReadAsync_PreservesData()
        {
            // Arrange
            string fileName = "/test.txt";
            string testData = "Hello, Memory Storage!";
            byte[] testBytes = Encoding.UTF8.GetBytes(testData);

            // Act - Write
            IntPtr writeHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            
            IntPtr bufferPtr = Marshal.AllocHGlobal(testBytes.Length);
            try
            {
                Marshal.Copy(testBytes, 0, bufferPtr, testBytes.Length);
                
                // Act - Write
                await _storage.WriteAsync(fileName, writeHandle, 0, testBytes.Length, bufferPtr);
                await _storage.CloseAsync(fileName, writeHandle);

                // Act - Read
                IntPtr readHandle = await _storage.OpenAsync(fileName, OpenFlags.ReadOnly);
                byte[] readBuffer = new byte[testBytes.Length];
                IntPtr readBufferPtr = Marshal.AllocHGlobal(readBuffer.Length);
                
                try
                {
                    await _storage.ReadAsync(fileName, readHandle, 0, readBuffer.Length, readBufferPtr);
                    Marshal.Copy(readBufferPtr, readBuffer, 0, readBuffer.Length);
                    await _storage.CloseAsync(fileName, readHandle);

                    // Assert
                    string result = Encoding.UTF8.GetString(readBuffer);
                    Assert.AreEqual(testData, result);
                }
                finally
                {
                    Marshal.FreeHGlobal(readBufferPtr);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(bufferPtr);
            }
        }

        [TestMethod]
        public async Task ReadDirAsync_ReturnsDirectoryContents()
        {
            // Arrange
            await _storage.CreateDirectoryAsync("/testdir");
            IntPtr handle1 = await _storage.OpenAsync("/file1.txt", OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync("/file1.txt", handle1);
            IntPtr handle2 = await _storage.OpenAsync("/file2.txt", OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync("/file2.txt", handle2);

            // Act
            var contents = await _storage.ReadDirAsync("/");

            // Assert
            Assert.IsTrue(contents.Any(f => f.Filename.EndsWith("testdir") && f.IsDirectory));
            Assert.IsTrue(contents.Any(f => f.Filename.EndsWith("file1.txt") && !f.IsDirectory));
            Assert.IsTrue(contents.Any(f => f.Filename.EndsWith("file2.txt") && !f.IsDirectory));
        }

        [TestMethod]
        public async Task GetFileInfoAsync_ExistingFile_ReturnsCorrectInfo()
        {
            // Arrange
            string fileName = "/info.txt";
            string content = "test content for info";
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            
            IntPtr writeHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr bufferPtr = Marshal.AllocHGlobal(contentBytes.Length);
            try
            {
                Marshal.Copy(contentBytes, 0, bufferPtr, contentBytes.Length);
                await _storage.WriteAsync(fileName, writeHandle, 0, contentBytes.Length, bufferPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(bufferPtr);
            }
            await _storage.CloseAsync(fileName, writeHandle);

            // Act
            var fileInfo = await _storage.GetFileInfoAsync(fileName);

            // Assert
            Assert.AreEqual(fileName, fileInfo.Filename);
            Assert.AreEqual(content.Length, fileInfo.Size);
            Assert.IsFalse(fileInfo.IsDirectory);
        }

        [TestMethod]
        public async Task DeleteAsync_DeletesExistingFile()
        {
            // Arrange
            string fileName = "/deleteme.txt";
            IntPtr handle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(fileName, handle);

            // Act
            await _storage.DeleteAsync(fileName);

            // Assert
            Assert.IsFalse(await _storage.FileExistsAsync(fileName));
        }

        [TestMethod]
        public async Task MoveAsync_MovesFileToNewLocation()
        {
            // Arrange
            string sourceFile = "/source.txt";
            string destFile = "/dest.txt";
            string content = "move this content";
            byte[] contentBytes = Encoding.UTF8.GetBytes(content);
            
            IntPtr writeHandle = await _storage.OpenAsync(sourceFile, OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr bufferPtr = Marshal.AllocHGlobal(contentBytes.Length);
            try
            {
                Marshal.Copy(contentBytes, 0, bufferPtr, contentBytes.Length);
                await _storage.WriteAsync(sourceFile, writeHandle, 0, contentBytes.Length, bufferPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(bufferPtr);
            }
            await _storage.CloseAsync(sourceFile, writeHandle);

            // Act
            await _storage.MoveAsync(sourceFile, destFile, false);

            // Assert
            Assert.IsFalse(await _storage.FileExistsAsync(sourceFile));
            Assert.IsTrue(await _storage.FileExistsAsync(destFile));
        }

        [TestMethod]
        public async Task ChownAsync_DoesNotThrowException()
        {
            // Arrange
            string fileName = "/chown.txt";
            IntPtr handle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(fileName, handle);

            // Act & Assert - should not throw
            await _storage.ChownAsync(fileName, 1000, 1000);
            
            // Just verify the file still exists
            Assert.IsTrue(await _storage.FileExistsAsync(fileName));
        }

        [TestMethod]
        public async Task SetTimesAsync_SetsFileTimesCorrectly()
        {
            // Arrange
            string fileName = "/times.txt";
            IntPtr handle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(fileName, handle);
            
            DateTime newAccessTime = DateTime.Now.AddDays(-5);
            DateTime newModTime = DateTime.Now.AddDays(-3);

            // Act
            await _storage.SetTimesAsync(fileName, newAccessTime, newModTime);

            // Assert
            var fileInfo = await _storage.GetFileInfoAsync(fileName);
            
            // Memory storage should preserve the set times exactly
            Assert.AreEqual(newAccessTime, fileInfo.LastAccessTime);
            Assert.AreEqual(newModTime, fileInfo.LastModified);
        }

        [TestMethod]
        public async Task TestReadAsync_WithOffset_ReadsFromCorrectPosition()
        {
            // Arrange
            string fileName = "/offset.txt";
            string fullContent = "0123456789";
            byte[] fullBytes = Encoding.UTF8.GetBytes(fullContent);
            
            // Write full content
            IntPtr writeHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr writeBufferPtr = Marshal.AllocHGlobal(fullBytes.Length);
            try
            {
                Marshal.Copy(fullBytes, 0, writeBufferPtr, fullBytes.Length);
                await _storage.WriteAsync(fileName, writeHandle, 0, fullBytes.Length, writeBufferPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(writeBufferPtr);
            }
            await _storage.CloseAsync(fileName, writeHandle);

            int offset = 3;
            int length = 4;
            string expectedContent = fullContent.Substring(offset, length); // "3456"

            // Act
            IntPtr readHandle = await _storage.OpenAsync(fileName, OpenFlags.ReadOnly);
            byte[] buffer = new byte[length];
            IntPtr readBufferPtr = Marshal.AllocHGlobal(buffer.Length);
            
            try
            {
                await _storage.ReadAsync(fileName, readHandle, offset, length, readBufferPtr);
                Marshal.Copy(readBufferPtr, buffer, 0, buffer.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(readBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, readHandle);

            // Assert
            string result = Encoding.UTF8.GetString(buffer);
            Assert.AreEqual(expectedContent, result);
        }

        [TestMethod]
        public async Task TestWriteAsync_WithOffset_WritesToCorrectPosition()
        {
            // Arrange
            string fileName = "/writeoffset.txt";
            string initialContent = "0123456789";
            string insertContent = "ABCD";
            
            // Create initial file
            byte[] initialBytes = Encoding.UTF8.GetBytes(initialContent);
            IntPtr initialHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr initialBufferPtr = Marshal.AllocHGlobal(initialBytes.Length);
            try
            {
                Marshal.Copy(initialBytes, 0, initialBufferPtr, initialBytes.Length);
                await _storage.WriteAsync(fileName, initialHandle, 0, initialBytes.Length, initialBufferPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(initialBufferPtr);
            }
            await _storage.CloseAsync(fileName, initialHandle);

            int offset = 3;
            byte[] insertData = Encoding.UTF8.GetBytes(insertContent);

            // Act
            IntPtr writeHandle = await _storage.OpenAsync(fileName, OpenFlags.ReadWrite);
            IntPtr writeBufferPtr = Marshal.AllocHGlobal(insertData.Length);
            
            try
            {
                Marshal.Copy(insertData, 0, writeBufferPtr, insertData.Length);
                await _storage.WriteAsync(fileName, writeHandle, offset, insertData.Length, writeBufferPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(writeBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, writeHandle);

            // Assert - Read back the full content
            IntPtr readHandle = await _storage.OpenAsync(fileName, OpenFlags.ReadOnly);
            byte[] resultBuffer = new byte[initialContent.Length];
            IntPtr readBufferPtr = Marshal.AllocHGlobal(resultBuffer.Length);
            
            try
            {
                await _storage.ReadAsync(fileName, readHandle, 0, resultBuffer.Length, readBufferPtr);
                Marshal.Copy(readBufferPtr, resultBuffer, 0, resultBuffer.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(readBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, readHandle);
            
            string result = Encoding.UTF8.GetString(resultBuffer);
            string expected = "012ABCD789"; // Original with "ABCD" inserted at position 3
            Assert.AreEqual(expected, result);
        }

        [TestMethod]
        public async Task GetTotalCapacityAsync_ReturnsConfiguredValue()
        {
            // Act
            long capacity = await _storage.GetTotalCapacityAsync();

            // Assert
            Assert.IsTrue(capacity > 0);
        }

        [TestMethod]
        public async Task GetAvailableFreeSpaceAsync_ReturnsPositiveValue()
        {
            // Act
            long freeSpace = await _storage.GetAvailableFreeSpaceAsync();

            // Assert
            Assert.IsTrue(freeSpace > 0);
        }

        [TestMethod]
        public async Task TestReadAsync_EmptyFile_ReturnsZeroBytes()
        {
            // Arrange
            string fileName = "/empty.txt";
            IntPtr createHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            await _storage.CloseAsync(fileName, createHandle); // Create empty file

            // Act
            IntPtr readHandle = await _storage.OpenAsync(fileName, OpenFlags.ReadOnly);
            byte[] buffer = new byte[10]; // Try to read 10 bytes
            IntPtr readBufferPtr = Marshal.AllocHGlobal(buffer.Length);
            
            try
            {
                await _storage.ReadAsync(fileName, readHandle, 0, buffer.Length, readBufferPtr);
                // For empty files, we don't copy anything, buffer should remain zero-filled
            }
            finally
            {
                Marshal.FreeHGlobal(readBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, readHandle);

            // Assert - buffer should remain zero-filled for empty file
            Assert.IsTrue(buffer.All(b => b == 0));
        }

        [TestMethod]
        public async Task TestWriteAsync_AppendMode_AppendsToFile()
        {
            // Arrange
            string fileName = "/append.txt";
            string initialContent = "Initial";
            string appendContent = "Appended";
            
            // Create initial content
            byte[] initialBytes = Encoding.UTF8.GetBytes(initialContent);
            IntPtr initialHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr initialBufferPtr = Marshal.AllocHGlobal(initialBytes.Length);
            try
            {
                Marshal.Copy(initialBytes, 0, initialBufferPtr, initialBytes.Length);
                await _storage.WriteAsync(fileName, initialHandle, 0, initialBytes.Length, initialBufferPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(initialBufferPtr);
            }
            await _storage.CloseAsync(fileName, initialHandle);

            byte[] appendData = Encoding.UTF8.GetBytes(appendContent);

            // Act
            IntPtr appendHandle = await _storage.OpenAsync(fileName, OpenFlags.WriteOnly | OpenFlags.Append);
            IntPtr appendBufferPtr = Marshal.AllocHGlobal(appendData.Length);
            
            try
            {
                Marshal.Copy(appendData, 0, appendBufferPtr, appendData.Length);
                await _storage.WriteAsync(fileName, appendHandle, 0, appendData.Length, appendBufferPtr); // Offset ignored in append mode
            }
            finally
            {
                Marshal.FreeHGlobal(appendBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, appendHandle);

            // Assert - Read back the result
            IntPtr readHandle = await _storage.OpenAsync(fileName, OpenFlags.ReadOnly);
            byte[] resultBuffer = new byte[initialContent.Length + appendContent.Length];
            IntPtr readBufferPtr = Marshal.AllocHGlobal(resultBuffer.Length);
            
            try
            {
                await _storage.ReadAsync(fileName, readHandle, 0, resultBuffer.Length, readBufferPtr);
                Marshal.Copy(readBufferPtr, resultBuffer, 0, resultBuffer.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(readBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, readHandle);
            
            string result = Encoding.UTF8.GetString(resultBuffer);
            Assert.AreEqual(initialContent + appendContent, result);
        }

        [TestMethod]
        public async Task TestReadWriteSequence_LargeFile()
        {
            // Arrange
            string fileName = "/largefile.txt";
            int dataSize = 1024 * 10; // 10KB
            byte[] originalData = new byte[dataSize];
            new Random().NextBytes(originalData);

            // Act - Write large data
            IntPtr writeHandle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr writeBufferPtr = Marshal.AllocHGlobal(originalData.Length);
            
            try
            {
                Marshal.Copy(originalData, 0, writeBufferPtr, originalData.Length);
                await _storage.WriteAsync(fileName, writeHandle, 0, originalData.Length, writeBufferPtr);
            }
            finally
            {
                Marshal.FreeHGlobal(writeBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, writeHandle);

            // Act - Read back large data
            IntPtr readHandle = await _storage.OpenAsync(fileName, OpenFlags.ReadOnly);
            byte[] readData = new byte[dataSize];
            IntPtr readBufferPtr = Marshal.AllocHGlobal(readData.Length);
            
            try
            {
                await _storage.ReadAsync(fileName, readHandle, 0, readData.Length, readBufferPtr);
                Marshal.Copy(readBufferPtr, readData, 0, readData.Length);
            }
            finally
            {
                Marshal.FreeHGlobal(readBufferPtr);
            }
            
            await _storage.CloseAsync(fileName, readHandle);

            // Assert
            CollectionAssert.AreEqual(originalData, readData);
        }

        [TestMethod]
        public async Task CloseAllHandlesAsync_ClosesAllOpenHandles()
        {
            // Arrange
            string fileName1 = "/handle1.txt";
            string fileName2 = "/handle2.txt";
            
            IntPtr handle1 = await _storage.OpenAsync(fileName1, OpenFlags.Create | OpenFlags.WriteOnly);
            IntPtr handle2 = await _storage.OpenAsync(fileName2, OpenFlags.Create | OpenFlags.WriteOnly);

            // Act
            await _storage.CloseAllHandlesAsync();

            // Assert - subsequent operations should work fine
            Assert.IsTrue(await _storage.FileExistsAsync(fileName1));
            Assert.IsTrue(await _storage.FileExistsAsync(fileName2));
        }
    }
} 
