using Microsoft.VisualStudio.TestTools.UnitTesting;
using StorageLib.Utilities;

namespace StorageLib.Tests.Utilities;

[TestClass]
public class PathHelperTests
{
    [TestMethod]
    [DataRow("C:\\folder\\file.txt", "/folder/file.txt")]          // Windows drive removed, converted to Linux-style
    [DataRow("folder/file.txt", "/folder/file.txt")]              // Mixed slashes, no drive
    [DataRow("/folder/file.txt", "/folder/file.txt")]             // Leading slash, no drive
    [DataRow("folder\\file.txt", "/folder/file.txt")]             // Backslashes, no drive
    [DataRow("\\folder\\file.txt", "/folder/file.txt")]           // Leading backslash, no drive
    [DataRow("file.txt", "/file.txt")]                            // Root file, no drive
    [DataRow("C:\\folder\\", "/folder")]                          // Trailing slash removed
    [DataRow("C:\\folder./file.txt", "/folder/file.txt")]         // Current dir .
    [DataRow("C:\\folder/./file.txt", "/folder/file.txt")]        // Mixed Current dir .
    [DataRow("C:\\folder\\..\\file.txt", "/file.txt")]            // Parent navigation with drive
    [DataRow("folder\\..\\file.txt", "/file.txt")]                // Parent navigation without drive
    [DataRow("/folder/../file.txt", "/file.txt")]                 // Parent navigation root
    [DataRow("C:/../file.txt", "/file.txt")]                      // Parent navigation above drive root
    [DataRow("/../file.txt", "/file.txt")]                        // Parent navigation above root
    [DataRow("", "/")]                                             // Empty path -> root
    [DataRow(null, "/")]                                           // Null path -> root
    [DataRow("   ", "/")]                                          // Whitespace -> root
    [DataRow("/", "/")]                                            // Root slash
    [DataRow("\\", "/")]                                           // Root backslash -> root slash
    [DataRow("C:", "/")]                                           // Drive letter only -> root (storage style)
    [DataRow("C:\\", "/")]                                         // Drive root with backslash -> root
    [DataRow("C:/", "/")]                                          // Drive root with forward slash -> root
    [DataRow("\\\\server\\share", "//server/share")]              // Basic UNC (preserved)
    [DataRow("//server/share/", "//server/share")]                // UNC with trailing slash
    [DataRow("//server/share/folder", "//server/share/folder")]   // UNC with folder
    [DataRow("//server/share/folder/./file.txt", "//server/share/folder/file.txt")]  // UNC with path traversal
    [DataRow("//server/share/folder/../file.txt", "//server/share/file.txt")]        // UNC parent navigation
    [DataRow("//server", "//server")]                             // Invalid UNC (no share) - returned as is
    [DataRow("//server/", "//server")]                            // Invalid UNC with slash
    public void NormalizePath_LinuxStyleDefault_ReturnsExpected(string input, string expected)
    {
        // Act (StorageLib defaults to Linux-style for storage operations)
        string result = PathHelper.NormalizePath(input);
        
        // Assert
        Assert.AreEqual(expected, result);
    }



    [TestMethod]
    [DataRow("C:\\folder\\file.txt", "/folder/file.txt")]         // Escaped backslashes (matching LayerHelper test)
    [DataRow("D:/folder/subdir/", "/folder/subdir")]              // Forward slashes are fine, trailing slash removed
    [DataRow("/folder/file.txt", "/folder/file.txt")]             // Already no drive
    [DataRow("\\\\server\\share\\file", "\\\\server\\share\\file")] // UNC paths preserved as-is
    [DataRow("C:", "/")]                                           // Drive only -> root
    [DataRow("C:\\", "/")]                                         // Drive with backslash -> root
    [DataRow("", "")]                                              // Empty -> empty
    [DataRow(null, null)]                                          // Null -> null
    [DataRow("relative/path", "relative/path")]                    // Relative paths unchanged
    public void RemoveDriveLetter_ReturnsExpected(string input, string expected)
    {
        // Act
        string result = PathHelper.RemoveDriveLetter(input);
        
        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    // Windows Style Inputs (drive letters removed, normalized to Linux-style)
    [DataRow("C:\\Folder\\File.txt", "/Folder/File.txt")]          // Windows path with drive
    [DataRow("D:/Mixed/Slashes.dat", "/Mixed/Slashes.dat")]        // Mixed slashes with drive
    [DataRow("FolderOnly", "/FolderOnly")]                         // Relative folder (no drive)
    [DataRow("C:\\Folder\\..\\Sibling.txt", "/Sibling.txt")]       // Path traversal with drive
    [DataRow("C:\\Folder\\", "/Folder")]                           // Trailing slash with drive
    [DataRow("C:", "/")]                                            // Drive root -> storage root
    // Linux Style Inputs (already Linux-style, just normalized)
    [DataRow("/home/user/file.sh", "/home/user/file.sh")]          // Linux absolute path
    [DataRow("relative/path", "/relative/path")]                   // Relative path normalized
    [DataRow("/a/b/../c", "/a/c")]                                  // Path traversal
    [DataRow("/a/b/", "/a/b")]                                      // Trailing slash removed
    [DataRow("/", "/")]                                             // Root stays root
    [DataRow("", "/")]                                              // Empty input -> root
    [DataRow(null, "/")]                                            // Null input -> root
    public void NormalizePathWithoutDriveLetter_ReturnsExpected(string input, string expected)
    {
        // Act
        string result = PathHelper.NormalizePathWithoutDriveLetter(input);
        
        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow("", "file.txt", "file.txt")]
    [DataRow(null, "file.txt", "file.txt")]
    [DataRow("/base", "", "/base")]
    [DataRow("/base", null, "/base")]
    [DataRow("/base", "file.txt", "/base/file.txt")]
    [DataRow("/base/", "/file.txt", "/base/file.txt")]
    [DataRow("/base", "subfolder/file.txt", "/base/subfolder/file.txt")]
    [DataRow("/base/", "subfolder/file.txt", "/base/subfolder/file.txt")]
    public void CombinePaths_ReturnsExpected(string basePath, string relativePath, string expected)
    {
        // Act
        string result = PathHelper.CombinePaths(basePath, relativePath);
        
        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    [DataRow("/folder/subfolder/file.txt", "/folder/subfolder")]   // Standard path
    [DataRow("/folder/file.txt", "/folder")]                       // File in folder
    [DataRow("/file.txt", "/")]                                     // File in root
    [DataRow("/folder/subfolder/", "/folder")]                     // Directory path with trailing slash (normalized)
    [DataRow("/folder", "/")]                                       // Directory in root
    [DataRow("/", "/")]                                             // Root directory itself
    [DataRow("file_in_current_dir", "")]                           // No directory part (relative)
    [DataRow("", "")]                                               // Empty path
    [DataRow(null, null)]                                           // Null input
    public void GetParentDirectory_LinuxStyle_ReturnsExpected(string? input, string? expected)
    {
        // Act (StorageLib defaults to Linux-style for storage operations)
        string? result = PathHelper.GetParentDirectory(input);
        
        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void NormalizePath_ComplexScenarios_WorkCorrectly()
    {
        // Complex path traversal (Linux-style for storage)
        Assert.AreEqual("/result", PathHelper.NormalizePath("/folder/../result"));
        
        // Multiple traversals
        Assert.AreEqual("/", PathHelper.NormalizePath("/a/b/../../"));
        
        // Mixed slashes - converted to Linux-style
        Assert.AreEqual("/folder/file", PathHelper.NormalizePath("\\folder/file"));
        
        // Dots and slashes  
        Assert.AreEqual("/messy/clean/path", PathHelper.NormalizePath("/messy/./path/../clean/path"));
    }
} 