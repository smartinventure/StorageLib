using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Connectors;

namespace StorageLib.Tests.Connectors;

[TestClass]
public class SshPathTests
{
    [TestMethod]
    public void ToVirtual_PathWithinBase_ReturnsVirtualPath()
    {
        Assert.AreEqual("/", SshPath.ToVirtual("/remote/base", "/remote/base"));
        Assert.AreEqual("/folder/file.txt", SshPath.ToVirtual(
            "/remote/base/",
            "/remote/base/folder/file.txt"));
        Assert.AreEqual("/folder/file.txt", SshPath.ToVirtual(
            "/",
            "/folder/file.txt"));
    }

    [TestMethod]
    [DataRow("/other/file.txt")]
    [DataRow("/remote/base-other/file.txt")]
    public void ToVirtual_PathOutsideBase_ThrowsArgumentException(string absolutePath)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => SshPath.ToVirtual("/remote/base", absolutePath));
    }

    [TestMethod]
    [DataRow("/remote/base/../escape")]
    [DataRow("remote/base/file.txt")]
    public void ToVirtual_UnsafeOrRelativePath_ThrowsArgumentException(string absolutePath)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => SshPath.ToVirtual("/remote/base", absolutePath));
    }
}
