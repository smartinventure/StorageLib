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
    public void CreateProcess_Writable_ServesWithAWriteCache()
    {
        // Served in rclone's default mode, a file has to be written strictly front to back and cannot be
        // open for reading and writing at once, which is how a filesystem writes nearly everything.
        var options = new RcloneConnectionOptions
        {
            RemoteName = "remote",
            ExecutablePath = "rclone",
            ConfigPath = "config",
            WriteBackSeconds = 7,
            WriteCacheSizeMegabytes = 2048,
            WriteCacheMaxAgeHours = 3
        };

        using var process = RcloneStorage.CreateProcess(Request(options));
        var arguments = process.StartInfo.ArgumentList;

        CollectionAssert.IsSubsetOf(
            new[] { "--vfs-cache-mode", "writes", "--vfs-write-back", "7s", "--vfs-cache-max-size", "2048M", "--vfs-cache-max-age", "3h" },
            arguments.ToArray());
    }

    [TestMethod]
    public void CreateProcess_WritableWithoutAWriteCache_ServesStraightThrough()
    {
        // Nothing may be held back here: closing a file has to mean the remote has accepted it.
        var options = new RcloneConnectionOptions
        {
            RemoteName = "remote",
            ExecutablePath = "rclone",
            ConfigPath = "config",
            UseWriteCache = false
        };

        using var process = RcloneStorage.CreateProcess(Request(options));
        var arguments = process.StartInfo.ArgumentList;

        CollectionAssert.IsSubsetOf(new[] { "--vfs-cache-mode", "off" }, arguments.ToArray());
        Assert.IsFalse(arguments.Contains("--vfs-write-back"),
            "There is no queue to write back from.");
    }

    [TestMethod]
    public void CreateProcess_PinnedCacheFolder_IsPassedToRclone()
    {
        string cacheFolder = Path.Combine(Path.GetTempPath(), "fm-rclone-cache");
        var options = new RcloneConnectionOptions
        {
            RemoteName = "remote",
            ExecutablePath = "rclone",
            ConfigPath = "config",
            CacheFolder = cacheFolder
        };

        using var process = RcloneStorage.CreateProcess(Request(options));
        var arguments = process.StartInfo.ArgumentList;

        CollectionAssert.IsSubsetOf(new[] { "--cache-dir", cacheFolder }, arguments.ToArray());
    }

    [TestMethod]
    public void CreateProcess_WithRemoteControl_KeepsCredentialsOutOfTheCommandLine()
    {
        var options = new RcloneConnectionOptions
        {
            RemoteName = "remote",
            ExecutablePath = "rclone",
            ConfigPath = "config"
        };
        var remoteControl = new RcloneEndpoint(5722, "rcuser", "rcpassword");

        using var process = RcloneStorage.CreateProcess(
            new RcloneProcessRequest(options, "remote:", new RcloneEndpoint(2022, "user", "password"), remoteControl));
        var arguments = process.StartInfo.ArgumentList;

        CollectionAssert.IsSubsetOf(new[] { "--rc", "--rc-addr", "127.0.0.1:5722" }, arguments.ToArray());
        Assert.IsFalse(arguments.Contains("rcpassword"), "Another user can read a command line.");
        Assert.AreEqual("rcuser", process.StartInfo.Environment["RCLONE_RC_USER"]);
        Assert.AreEqual("rcpassword", process.StartInfo.Environment["RCLONE_RC_PASS"]);
    }

    [TestMethod]
    public void CreateProcess_WithoutRemoteControl_StartsNoControlInterface()
    {
        var options = new RcloneConnectionOptions
        {
            RemoteName = "remote",
            ExecutablePath = "rclone",
            ConfigPath = "config"
        };

        using var process = RcloneStorage.CreateProcess(Request(options));

        Assert.IsFalse(process.StartInfo.ArgumentList.Contains("--rc"));
    }

    [TestMethod]
    public async Task GetPendingUploadsAsync_NotInitialized_ReportsNothingPending()
    {
        using var storage = new RcloneStorage();

        var pending = await storage.GetPendingUploadsAsync();

        Assert.AreEqual(PendingUploadState.None, pending);
        Assert.IsFalse(pending.HasPendingUploads);
    }

    [TestMethod]
    public async Task InitializeAsync_CacheFolderThatIsNotAbsolute_ThrowsArgumentException()
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
                CacheFolder = "relative\\cache"
            };
            using var storage = new RcloneStorage();

            await Assert.ThrowsExactlyAsync<ArgumentException>(
                () => storage.InitializeAsync(JsonSerializer.Serialize(options), string.Empty));
        }
        finally
        {
            File.Delete(executablePath);
            File.Delete(configPath);
        }
    }

    [TestMethod]
    public void CreateProcess_ReadOnly_ServesWithoutAWriteCache()
    {
        var options = new RcloneConnectionOptions
        {
            RemoteName = "remote",
            ExecutablePath = "rclone",
            ConfigPath = "config",
            IsReadOnly = true
        };

        using var process = RcloneStorage.CreateProcess(Request(options));
        var arguments = process.StartInfo.ArgumentList;

        Assert.IsTrue(arguments.Contains("--read-only"));
        Assert.IsFalse(arguments.Contains("--vfs-cache-mode"),
            "There is nothing to buffer when nothing can be written.");
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

    private static RcloneProcessRequest Request(RcloneConnectionOptions options) =>
        new(options, "remote:", new RcloneEndpoint(2022, "user", "password"), null);
}
