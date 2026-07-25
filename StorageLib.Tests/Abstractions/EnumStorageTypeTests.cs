using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Abstractions;

namespace StorageLib.Tests.Abstractions;

[TestClass]
public class EnumStorageTypeTests
{
    [TestMethod]
    public void EnumStorageType_HasExpectedValues()
    {
        // Test that all expected storage types are defined with correct values
        Assert.AreEqual(0, (int)EnumStorageType.Local);
        Assert.AreEqual(1, (int)EnumStorageType.Memory);
        Assert.AreEqual(2, (int)EnumStorageType.AWS);
        Assert.AreEqual(3, (int)EnumStorageType.Azure);
        Assert.AreEqual(4, (int)EnumStorageType.GoogleDrive);
        Assert.AreEqual(5, (int)EnumStorageType.OneDrive);
        Assert.AreEqual(6, (int)EnumStorageType.FTP);
        Assert.AreEqual(7, (int)EnumStorageType.SSH);
        Assert.AreEqual(8, (int)EnumStorageType.OpenSlide);
        Assert.AreEqual(9, (int)EnumStorageType.WindowsMetaMemory);
        Assert.AreEqual(10, (int)EnumStorageType.Rclone);
    }

    [TestMethod]
    public void EnumStorageType_CanConvertToString()
    {
        // Test string conversion
        Assert.AreEqual("Local", EnumStorageType.Local.ToString());
        Assert.AreEqual("Memory", EnumStorageType.Memory.ToString());
        Assert.AreEqual("AWS", EnumStorageType.AWS.ToString());
        Assert.AreEqual("Azure", EnumStorageType.Azure.ToString());
        Assert.AreEqual("SSH", EnumStorageType.SSH.ToString());
        Assert.AreEqual("Rclone", EnumStorageType.Rclone.ToString());
    }

    [TestMethod]
    public void EnumStorageType_CanParseFromString()
    {
        // Test parsing from string
        Assert.IsTrue(Enum.TryParse<EnumStorageType>("Local", out var localType));
        Assert.AreEqual(EnumStorageType.Local, localType);
        
        Assert.IsTrue(Enum.TryParse<EnumStorageType>("Azure", out var azureType));
        Assert.AreEqual(EnumStorageType.Azure, azureType);
        
        Assert.IsTrue(Enum.TryParse<EnumStorageType>("SSH", out var sshType));
        Assert.AreEqual(EnumStorageType.SSH, sshType);

        Assert.IsTrue(Enum.TryParse<EnumStorageType>("Rclone", out var rcloneType));
        Assert.AreEqual(EnumStorageType.Rclone, rcloneType);
    }
} 