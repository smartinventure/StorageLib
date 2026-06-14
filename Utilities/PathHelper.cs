using System.Text.RegularExpressions;

namespace StorageLib.Utilities
{
    /// <summary>
    /// Operating system types for path normalization
    /// </summary>
    public enum OperatingSystem
    {
        Windows,
        Linux,
        MacOS
    }

    /// <summary>
    /// Universal path helper utilities for storage operations.
    /// Provides cross-platform path normalization and manipulation.
    /// </summary>
    public static class PathHelper
    {
        /// <summary>
        /// Normalizes a path to Linux-style (forward slashes, no drive letters).
        /// Drive letters are only relevant at mount time, after that everything is relative.
        /// Automatically detects Windows vs Linux paths and normalizes accordingly, then converts to Linux-style.
        /// </summary>
        public static string NormalizePath(string path)
        {
            // For simple Linux-style paths, just use Linux normalization
            // For Windows paths (drive letters, UNC), normalize as Windows first, then convert
            return NormalizePathWithoutDriveLetter(path);
        }

        /// <summary>
        /// Normalizes a path with OS-specific handling (used internally for mount operations).
        /// This is the exact same logic from LayerHelper.NormalizePath that works correctly.
        /// </summary>
        internal static string NormalizePathInternal(string path, OperatingSystem os)
        {
            // 1. Handle null/empty/whitespace
            if (string.IsNullOrWhiteSpace(path))
                return "/"; // Return root for empty/null paths

            // 2. Convert to forward slashes consistently
            string normalized = path.Replace('\\', '/');

            // 3. Handle UNC paths (Windows only)
            string uncPrefix = string.Empty;
            if (os == OperatingSystem.Windows && normalized.StartsWith("//"))
            {
                // Find the end of the //server/share part
                int firstSlashAfterHost = normalized.IndexOf('/', 2); // Find slash after server name
                if (firstSlashAfterHost > 0)
                {
                    int secondSlashAfterHost = normalized.IndexOf('/', firstSlashAfterHost + 1); // Find slash after share name
                    if (secondSlashAfterHost > 0)
                    {
                        // Valid UNC prefix found: //server/share
                        uncPrefix = normalized.Substring(0, secondSlashAfterHost);
                        normalized = normalized.Substring(secondSlashAfterHost); // Path part after share
                    }
                    else // Case: //server/share (no trailing slash or path)
                    {
                        uncPrefix = normalized; // The whole thing is the prefix
                        normalized = string.Empty; // No path part
                    }
                }
                else // Case: //server (invalid UNC for file operations) or //server/
                {
                    // Return //server or //server/ (after trimming any trailing slash from original)
                    return path.Replace('\\', '/').TrimEnd('/');
                }
            }

            // 4. Handle Drive letters (Windows only, if not UNC)
            string drivePrefix = string.Empty;
            bool isJustDrive = false;
            if (os == OperatingSystem.Windows && string.IsNullOrEmpty(uncPrefix) && normalized.Length >= 2 && normalized[1] == ':')
            {
                drivePrefix = normalized.Substring(0, 1).ToUpper() + ":";
                if (normalized.Length == 2) // Path is "C:"
                {
                    isJustDrive = true;
                    normalized = string.Empty;
                }
                else if (normalized.Length == 3 && normalized[2] == '/') // Path is "C:/"
                {
                    isJustDrive = true;
                    normalized = string.Empty;
                }
                else
                {
                    // Path is "C:/folder..." or "C:folder..." (though C:folder is less common)
                    normalized = normalized.Substring(2);
                    if (normalized.StartsWith("/")) // Handles C:/folder
                    {
                        normalized = normalized.Substring(1);
                    }
                }
            }

            // If it was just a drive letter (e.g. "C:", "C:/"), return it as "C:"
            if (isJustDrive)
            {
                return drivePrefix;
            }

            // 5. Resolve "." and ".." components in the remaining 'normalized' path part
            var components = normalized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            var resolvedComponents = new List<string>();
            foreach (var component in components)
            {
                if (component == "..")
                {
                    if (resolvedComponents.Count > 0)
                    {
                        resolvedComponents.RemoveAt(resolvedComponents.Count - 1);
                    }
                }
                else if (component != ".")
                {
                    string cleanedComponent = component.TrimEnd('.');
                    if (!string.IsNullOrEmpty(cleanedComponent))
                    {
                        resolvedComponents.Add(cleanedComponent);
                    }
                }
            }

            // 6. Reconstruct the normalized path part
            string reconstructedPathPart = string.Join("/", resolvedComponents);

            // 7. Combine prefix (UNC or Drive) with the normalized path part
            if (!string.IsNullOrEmpty(uncPrefix))
            {
                // Combine UNC prefix with path part.
                // If path part is empty, result is just the UNC prefix (e.g. //server/share)
                // Otherwise, //server/share + / + pathpart
                normalized = uncPrefix + (resolvedComponents.Any() ? "/" + reconstructedPathPart : string.Empty);
            }
            else if (!string.IsNullOrEmpty(drivePrefix))
            {
                // Combine drive prefix with path part.
                // If path part is empty (e.g. input "C:", "C:/"), we already returned.
                // So, here reconstructedPathPart will not be empty for drive paths like "C:/folder"
                normalized = drivePrefix + "/" + reconstructedPathPart;
            }
            else // It's a standard Unix-style path or Windows relative path (no drive, no UNC)
            {
                if (!resolvedComponents.Any()) return "/"; // Root for inputs like "/", "/.", "/.."
                normalized = "/" + reconstructedPathPart;
            }

            // 8. Final check for trailing slashes (should generally be handled by reconstruction)
            // However, specific cases like UNC roots or single drive letters handled differently above.
            // If it's a UNC path and ends with reconstructedPathPart being empty, it means it's like //server/share, no trailing slash.
            // If it's a drive path with components, it's Drive:/comp1/comp2.
            // If it's a Unix path with components, it's /comp1/comp2.
            // If it's root, it's "/".
            // This step might be simplified or removed if the above logic is robust.
            if (normalized.Length > 1 && normalized.EndsWith('/') &&
                !isJustDrive && // Don't trim from "C:" if it were to reach here
                !(os == OperatingSystem.Windows && Regex.IsMatch(normalized, @"^[a-zA-Z]:/$")) && // Don't trim "C:/"
                !(normalized == uncPrefix && string.IsNullOrEmpty(reconstructedPathPart))) // Don't trim from "//server/share"
            {
                normalized = normalized.TrimEnd('/');
            }

            // Case: if input was "C:" or "C:/", already returned as "C:".
            // Case: if input was "C:/folder", normalized is "C:/folder".
            // Case: if input was "/", normalized is "/".
            // Case: if input was "//server/share", normalized is "//server/share".
            // Case: if input was "//server/share/", normalized is "//server/share".

            return normalized;
        }

        /// <summary>
        /// Removes Windows drive letter from a path if present.
        /// Useful for creating portable paths.
        /// </summary>
        public static string RemoveDriveLetter(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            // Check for UNC Path
            if (path.StartsWith("\\") || path.StartsWith("//"))
            {
                // For UNC paths, trim trailing slashes for incomplete UNC paths like "//server/"
                string trimmed = path.TrimEnd('/');
                return trimmed;
            }

            // Ensure forward slashes for non-UNC paths
            string normalized = path.Replace('\\', '/');

            // Check for Windows drive pattern C:/...
            if (normalized.Length >= 2 && normalized[1] == ':')
            {
                // If it's just "C:" or "C:/", return "/"
                if (normalized.Length <= 3 && (normalized.Length == 2 || normalized.EndsWith("/")))
                {
                    return "/";
                }
                else
                {
                    // Return the part after the drive, ensure leading slash
                    string remainder = normalized.Substring(2);
                    string result = remainder.StartsWith('/') ? remainder : "/" + remainder;
                    // Remove trailing slash unless it's just "/"
                    if (result.Length > 1 && result.EndsWith('/'))
                    {
                        result = result.TrimEnd('/');
                    }
                    return result;
                }
            }

            // If not a Windows drive path, return the forward-slashed path
            string nonDriveResult = normalized;
            if (nonDriveResult.Length > 1 && nonDriveResult.EndsWith('/'))
            {
                nonDriveResult = nonDriveResult.TrimEnd('/');
            }
            return nonDriveResult;
        }

        /// <summary>
        /// Normalizes a path and removes drive letter if present.
        /// This is the standard method for storage operations (always Linux-style).
        /// </summary>
        public static string NormalizePathWithoutDriveLetter(string path)
        {
            // Handle null/empty explicitly, return "/"
            if (string.IsNullOrWhiteSpace(path))
                return "/";

            // For storage operations, we need to handle Windows paths properly first
            // Detect if it's a Windows path (has drive or UNC) and normalize accordingly
            bool isWindowsPath = (!string.IsNullOrEmpty(path) && 
                                  (path.Length >= 2 && path[1] == ':') ||  // Drive letter
                                  IsValidUncPath(path)); // Valid UNC path only

            // First normalize the path with proper OS handling
            string normalizedPath = isWindowsPath 
                ? NormalizePathInternal(path, OperatingSystem.Windows)
                : NormalizePathInternal(path, OperatingSystem.Linux);

            // Then remove drive letters for storage operations (everything is relative after mounting)
            return RemoveDriveLetter(normalizedPath);
        }

        /// <summary>
        /// Combines two path segments safely, handling slashes correctly.
        /// </summary>
        public static string CombinePaths(string basePath, string relativePath)
        {
            if (string.IsNullOrEmpty(basePath))
                return relativePath ?? string.Empty;
            if (string.IsNullOrEmpty(relativePath))
                return basePath;

            // Ensure base path doesn't end with slash, relative doesn't start with slash
            string cleanBase = basePath.TrimEnd('/');
            string cleanRelative = relativePath.TrimStart('/');

            if (string.IsNullOrEmpty(cleanRelative))
                return cleanBase;

            return cleanBase + "/" + cleanRelative;
        }

        /// <summary>
        /// Checks if a path appears to be a UNC path (//server/share format)
        /// Only treats paths that look like real UNC paths as UNC paths
        /// For storage operations, be conservative and only treat obvious UNC paths as such
        /// </summary>
        private static bool IsValidUncPath(string path)
        {
            if (!path.StartsWith("\\\\") && !path.StartsWith("//"))
                return false;
                
            string normalized = path.Replace('\\', '/');
            
            // Must have at least //server/share format (minimum 5 characters)
            if (normalized.Length < 5)
                return false;
                
            // Find the server part - look for the first slash after the prefix
            int firstSlashAfterPrefix = normalized.IndexOf('/', 2);
            
            // Handle different cases
            if (firstSlashAfterPrefix == -1)
            {
                // //server format without trailing slash - treat as UNC if server doesn't look like filename
                if (normalized.Length > 2)
                {
                    string serverPart = normalized.Substring(2);
                    // If it looks like a filename (has extension), treat as regular path with extra slashes
                    return !serverPart.Contains('.');
                }
                return false;
            }
            else if (firstSlashAfterPrefix == 2)
            {
                // /// format - not UNC
                return false;
            }
            else if (firstSlashAfterPrefix == normalized.Length - 1)
            {
                // //server/ format - server with trailing slash but no share
                string serverPart = normalized.Substring(2, firstSlashAfterPrefix - 2);
                // If it looks like a filename (has extension), treat as regular path with extra slashes
                return !serverPart.Contains('.');
            }
            
            // Check if server part looks like a filename (has extension)
            string server = normalized.Substring(2, firstSlashAfterPrefix - 2);
            if (server.Contains('.'))
                return false; // Looks like a filename, not a server name
                
            // Must have a share part after the server
            if (firstSlashAfterPrefix >= normalized.Length - 1)
                return false; // No share part
                
            // Has proper server/share structure
            return true;
        }

        /// <summary>
        /// Gets the parent directory of a path (Linux-style for storage operations).
        /// Exact same logic as LayerHelper.GetParentDirectory but for Linux-style paths.
        /// </summary>
        public static string? GetParentDirectory(string? path)
        {
            // Test expects null for null input
            if (path == null)
                return null;
            // Test expects empty for empty input
            if (path == string.Empty)
                return "";

            // Normalize path first to handle slashes consistently (Linux-style for storage)
            string normalizedPath = NormalizePath(path);

            // If normalized path is a root, return root itself.
            // For storage operations, we only deal with "/" as root (no drive letters)
            if (normalizedPath == "/")
                return normalizedPath;

            // Find the last slash separator IN THE NORMALIZED PATH
            int lastSlash = normalizedPath.LastIndexOf('/');

            // Case 1: No slash found in original path (e.g., "file") -> parent is empty string ""
            if (!path.Contains('/') && !path.Contains('\\'))
            {
                // For Linux-style storage, "file" has parent ""
                return "";
            }

            // Case 2: Root slash (e.g., "/file") -> parent is root "/"
            if (lastSlash == 0)
            {
                return "/";
            }

            // Case 3: Normal path (e.g., "/folder/file", "/folder/subfolder/")
            if (lastSlash > 0)
            {
                // For paths like "/folder/subfolder/", normalizedPath is "/folder/subfolder" 
                // and LastIndexOf('/') correctly finds the slash before "subfolder".
                // Substring(0, lastSlash) gives "/folder".
                return normalizedPath.Substring(0, lastSlash);
            }

            // Fallback: Should ideally not be reached. Return root for Linux-style storage.
            return "/";
        }
    }
} 