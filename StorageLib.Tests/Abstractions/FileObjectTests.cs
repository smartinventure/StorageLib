using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Abstractions;

namespace StorageLib.Tests.Abstractions;

[TestClass]
public class FileObjectTests
{
    [TestMethod]
    public void FileObject_Constructor_SetsProperties()
    {
        // Arrange
        var virtualPath = "/test/file.txt";
        
        // Act
        var fileObject = new FileObject(virtualPath);
        
        // Assert
        Assert.AreEqual(virtualPath, fileObject.VirtualPath);
        Assert.IsNull(fileObject.SC);
        Assert.AreEqual(string.Empty, fileObject.RealPath);
        Assert.AreEqual(string.Empty, fileObject.Filename);
        Assert.AreEqual(0, fileObject.Size);
        Assert.IsFalse(fileObject.IsDirectory);
        Assert.IsFalse(fileObject.IsReadOnly);
    }

    [TestMethod]
    public void FileObject_Constructor_WithStorage_SetsProperties()
    {
        // Arrange
        var virtualPath = "/test/file.txt";
        // Note: We'll need a mock IStorage for this test later
        
        // Act
        var fileObject = new FileObject(virtualPath, null);
        
        // Assert
        Assert.AreEqual(virtualPath, fileObject.VirtualPath);
        Assert.IsNull(fileObject.SC);
    }

    [TestMethod]
    public void FileObject_Bytes_DefaultEmpty()
    {
        // Arrange & Act
        var fileObject = new FileObject("/test");
        
        // Assert
        Assert.IsNotNull(fileObject.Bytes);
        Assert.AreEqual(0, fileObject.Bytes.Length);
    }

    [TestMethod]
    public void FileObject_Bytes_CanBeSet()
    {
        // Arrange
        var fileObject = new FileObject("/test");
        var testData = new byte[] { 1, 2, 3, 4, 5 };
        
        // Act
        fileObject.Bytes = testData;
        
        // Assert
        Assert.AreEqual(testData, fileObject.Bytes);
        Assert.AreEqual(5, fileObject.Bytes.Length);
    }
} 