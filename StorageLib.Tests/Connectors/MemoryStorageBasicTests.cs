using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Connectors;
using StorageLib.Abstractions;
using System.Threading.Tasks;
using System;

namespace StorageLib.Tests.Connectors
{
    [TestClass]
    public class MemoryStorageBasicTests
    {
        private MemoryStorage _storage;

        [TestInitialize]
        public void Setup()
        {
            _storage = new MemoryStorage();
            _storage.InitializeAsync("", "/").Wait();
        }

        [TestMethod]
        public async Task OpenAsync_CreatesNewFile()
        {
            // Arrange
            string fileName = "/test.txt";

            // Act
            IntPtr handle = await _storage.OpenAsync(fileName, OpenFlags.Create | OpenFlags.WriteOnly);
            
            // Assert
            Assert.AreNotEqual(IntPtr.Zero, handle);
            Assert.IsTrue(await _storage.FileExistsAsync(fileName));

            // Cleanup
            await _storage.CloseAsync(fileName, handle);
        }
    }
} 