using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Abstractions;
using StorageLib.Connectors;
using StorageLib.Factory;

namespace StorageLib.Tests.Connectors;

[TestClass]
public class RcloneStorageTests
{
    [TestMethod]
    public void SpeedLimits_SetBeforeInitialization_ArePreserved()
    {
        using var storage = new RcloneStorage
        {
            MaxReadBytesPerSecond = 1234,
            MaxWriteBytesPerSecond = 5678
        };

        Assert.AreEqual(1234, storage.MaxReadBytesPerSecond);
        Assert.AreEqual(5678, storage.MaxWriteBytesPerSecond);
    }

    [TestMethod]
    public void RcloneConnectionOptions_DeleteConfigOnDispose_DefaultsFalse()
    {
        var options = new RcloneConnectionOptions();

        Assert.IsFalse(options.DeleteConfigOnDispose);
    }

    [TestMethod]
    public void StorageFactory_RcloneAndSsh_ReturnSupportedConnectors()
    {
        using IStorage ssh = StorageFactory.GetStorageConnector(EnumStorageType.SSH);
        using IStorage rclone = StorageFactory.GetStorageConnector(EnumStorageType.Rclone);

        Assert.IsInstanceOfType<SshStorage>(ssh);
        Assert.IsInstanceOfType<RcloneStorage>(rclone);
    }

    [TestMethod]
    public void BuildTarget_NormalizedSubpath_ReturnsRcloneTarget()
    {
        string target = RcloneStorage.BuildTarget("cloud.remote", "/folder\\child/");

        Assert.AreEqual("cloud.remote:folder/child", target);
        Assert.AreEqual("cloud.remote:", RcloneStorage.BuildTarget("cloud.remote", null));
    }

    [TestMethod]
    [DataRow("../escape")]
    [DataRow("folder/../escape")]
    [DataRow("C:/escape")]
    [DataRow("folder/./child")]
    public void BuildTarget_UnsafeSubpath_ThrowsArgumentException(string subpath)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => RcloneStorage.BuildTarget("remote", subpath));
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("bad:name")]
    [DataRow("../remote")]
    public void BuildTarget_InvalidRemoteName_ThrowsArgumentException(string remoteName)
    {
        Assert.ThrowsExactly<ArgumentException>(
            () => RcloneStorage.BuildTarget(remoteName, null));
    }

    [TestMethod]
    public async Task InitializeAsync_InvalidJson_ThrowsArgumentException()
    {
        using var storage = new RcloneStorage();

        await Assert.ThrowsExactlyAsync<ArgumentException>(
            () => storage.InitializeAsync("{", string.Empty));
    }

    [TestMethod]
    public async Task InitializeAsync_MissingExecutable_ThrowsFileNotFoundException()
    {
        string configPath = Path.GetTempFileName();
        try
        {
            var options = new RcloneConnectionOptions
            {
                RemoteName = "remote",
                ExecutablePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe"),
                ConfigPath = configPath,
                StartupTimeoutSeconds = 5
            };
            using var storage = new RcloneStorage();

            await Assert.ThrowsExactlyAsync<FileNotFoundException>(
                () => storage.InitializeAsync(JsonSerializer.Serialize(options), string.Empty));
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [TestMethod]
    public async Task InitializeAsync_OwnedConfigAndMissingExecutable_DeletesConfig()
    {
        string configPath = Path.GetTempFileName();
        var options = new RcloneConnectionOptions
        {
            RemoteName = "remote",
            ExecutablePath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.exe"),
            ConfigPath = configPath,
            DeleteConfigOnDispose = true,
            StartupTimeoutSeconds = 5
        };
        using var storage = new RcloneStorage();

        await Assert.ThrowsExactlyAsync<FileNotFoundException>(
            () => storage.InitializeAsync(JsonSerializer.Serialize(options), string.Empty));

        Assert.IsFalse(File.Exists(configPath));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(301)]
    public async Task InitializeAsync_InvalidTimeout_ThrowsArgumentOutOfRangeException(int timeout)
    {
        string executablePath = Path.GetTempFileName();
        string configPath = Path.GetTempFileName();
        try
        {
            var options = new RcloneConnectionOptions
            {
                RemoteName = "remote",
                ExecutablePath = executablePath,
                ConfigPath = configPath,
                StartupTimeoutSeconds = timeout
            };
            using var storage = new RcloneStorage();

            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(
                () => storage.InitializeAsync(JsonSerializer.Serialize(options), string.Empty));
        }
        finally
        {
            File.Delete(executablePath);
            File.Delete(configPath);
        }
    }
}
