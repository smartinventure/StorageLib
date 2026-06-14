using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Abstractions;

namespace StorageLib.Tests.Abstractions;

[TestClass]
public class FuseFlagsTests
{
    [TestMethod]
    public void FuseFlags_Constants_HaveCorrectValues()
    {
        // Test that POSIX constants have correct values
        Assert.AreEqual(0x0000, FuseFlags.O_RDONLY);
        Assert.AreEqual(0x0001, FuseFlags.O_WRONLY);
        Assert.AreEqual(0x0002, FuseFlags.O_RDWR);
        Assert.AreEqual(0x0040, FuseFlags.O_CREAT);
        Assert.AreEqual(0x0080, FuseFlags.O_EXCL);
        Assert.AreEqual(0x0200, FuseFlags.O_TRUNC);
        Assert.AreEqual(0x0400, FuseFlags.O_APPEND);
        Assert.AreEqual(0x10000, FuseFlags.O_DIRECTORY);
    }

    [TestMethod]
    public void FuseFlags_HasFlag_WorksCorrectly()
    {
        // Test single flags
        Assert.IsTrue(FuseFlags.HasFlag(FuseFlags.O_CREAT, FuseFlags.O_CREAT));
        Assert.IsFalse(FuseFlags.HasFlag(FuseFlags.O_RDONLY, FuseFlags.O_CREAT));
        
        // Test combined flags
        int combinedFlags = FuseFlags.O_CREAT | FuseFlags.O_EXCL;
        Assert.IsTrue(FuseFlags.HasFlag(combinedFlags, FuseFlags.O_CREAT));
        Assert.IsTrue(FuseFlags.HasFlag(combinedFlags, FuseFlags.O_EXCL));
        Assert.IsFalse(FuseFlags.HasFlag(combinedFlags, FuseFlags.O_TRUNC));
    }

    [TestMethod]
    public void FuseFlags_GetAccessMode_ReturnsCorrectMode()
    {
        // Test pure access modes
        Assert.AreEqual(FuseFlags.O_RDONLY, FuseFlags.GetAccessMode(FuseFlags.O_RDONLY));
        Assert.AreEqual(FuseFlags.O_WRONLY, FuseFlags.GetAccessMode(FuseFlags.O_WRONLY));
        Assert.AreEqual(FuseFlags.O_RDWR, FuseFlags.GetAccessMode(FuseFlags.O_RDWR));
        
        // Test access mode extraction from combined flags
        int combinedFlags = FuseFlags.O_RDWR | FuseFlags.O_CREAT | FuseFlags.O_TRUNC;
        Assert.AreEqual(FuseFlags.O_RDWR, FuseFlags.GetAccessMode(combinedFlags));
    }

    [TestMethod]
    public void FuseFlags_IsReadAccess_WorksCorrectly()
    {
        // Read-only access
        Assert.IsTrue(FuseFlags.IsReadAccess(FuseFlags.O_RDONLY));
        Assert.IsFalse(FuseFlags.IsReadAccess(FuseFlags.O_WRONLY));
        Assert.IsTrue(FuseFlags.IsReadAccess(FuseFlags.O_RDWR));
        
        // Combined with other flags
        Assert.IsTrue(FuseFlags.IsReadAccess(FuseFlags.O_RDONLY | FuseFlags.O_CREAT));
        Assert.IsTrue(FuseFlags.IsReadAccess(FuseFlags.O_RDWR | FuseFlags.O_TRUNC));
    }

    [TestMethod]
    public void FuseFlags_IsWriteAccess_WorksCorrectly()
    {
        // Write access tests
        Assert.IsFalse(FuseFlags.IsWriteAccess(FuseFlags.O_RDONLY));
        Assert.IsTrue(FuseFlags.IsWriteAccess(FuseFlags.O_WRONLY));
        Assert.IsTrue(FuseFlags.IsWriteAccess(FuseFlags.O_RDWR));
        
        // Combined with other flags
        Assert.IsTrue(FuseFlags.IsWriteAccess(FuseFlags.O_WRONLY | FuseFlags.O_CREAT));
        Assert.IsTrue(FuseFlags.IsWriteAccess(FuseFlags.O_RDWR | FuseFlags.O_APPEND));
    }

    [TestMethod]
    public void FuseFlags_RealWorldScenarios_WorkCorrectly()
    {
        // Scenario: Create new file for writing
        int createWrite = FuseFlags.O_WRONLY | FuseFlags.O_CREAT | FuseFlags.O_EXCL;
        Assert.IsTrue(FuseFlags.HasFlag(createWrite, FuseFlags.O_CREAT));
        Assert.IsTrue(FuseFlags.HasFlag(createWrite, FuseFlags.O_EXCL));
        Assert.IsTrue(FuseFlags.IsWriteAccess(createWrite));
        Assert.IsFalse(FuseFlags.IsReadAccess(createWrite));
        
        // Scenario: Open existing file for read/write with truncation
        int readWriteTrunc = FuseFlags.O_RDWR | FuseFlags.O_TRUNC;
        Assert.IsTrue(FuseFlags.HasFlag(readWriteTrunc, FuseFlags.O_TRUNC));
        Assert.IsTrue(FuseFlags.IsReadAccess(readWriteTrunc));
        Assert.IsTrue(FuseFlags.IsWriteAccess(readWriteTrunc));
        
        // Scenario: Append to file
        int appendWrite = FuseFlags.O_WRONLY | FuseFlags.O_APPEND;
        Assert.IsTrue(FuseFlags.HasFlag(appendWrite, FuseFlags.O_APPEND));
        Assert.IsTrue(FuseFlags.IsWriteAccess(appendWrite));
        Assert.IsFalse(FuseFlags.IsReadAccess(appendWrite));
    }
} 