using StorageLib.Abstractions;

namespace StorageLib.Connectors;

internal static class SshPath
{
    public static string Resolve(string baseFolder, string path)
    {
        Validate(path, nameof(path));
        string relative = path.Replace('\\', '/').Trim('/');
        return relative.Length == 0 || relative == "."
            ? baseFolder
            : baseFolder == "/" ? $"/{relative}" : $"{baseFolder}/{relative}";
    }

    public static string NormalizeBase(string path)
    {
        path = string.IsNullOrWhiteSpace(path) ? "/" : path;
        Validate(path, nameof(path));
        string normalized = "/" + path.Replace('\\', '/').Trim('/');
        return normalized.Length == 1 ? "/" : normalized;
    }

    public static string ToVirtual(string baseFolder, string absolutePath)
    {
        string normalizedBase = NormalizeBase(baseFolder);
        string normalizedPath = NormalizeAbsolute(absolutePath);
        if (normalizedPath == normalizedBase)
        {
            return "/";
        }

        string prefix = normalizedBase == "/" ? "/" : normalizedBase + "/";
        if (!normalizedPath.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new ArgumentException("The SFTP path is outside the configured base folder.", nameof(absolutePath));
        }

        return "/" + normalizedPath[prefix.Length..];
    }

    public static FileMode GetFileMode(OpenFlags flags)
    {
        if (flags.HasFlag(OpenFlags.Exclusive))
        {
            return FileMode.CreateNew;
        }

        if (flags.HasFlag(OpenFlags.Truncate))
        {
            return FileMode.Create;
        }

        return flags.HasFlag(OpenFlags.Create) ? FileMode.OpenOrCreate : FileMode.Open;
    }

    public static string? GetParent(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash <= 0 ? null : path[..slash];
    }

    public static string GetName(string path)
    {
        int slash = path.LastIndexOf('/');
        return slash < 0 ? path : path[(slash + 1)..];
    }

    private static void Validate(string path, string parameterName)
    {
        if (path.Contains('\0') ||
            path.Replace('\\', '/').Split('/').Any(segment => segment == ".."))
        {
            throw new ArgumentException("Path traversal is not allowed.", parameterName);
        }
    }

    private static string NormalizeAbsolute(string path)
    {
        Validate(path, nameof(path));
        string normalized = path.Replace('\\', '/');
        if (!normalized.StartsWith('/'))
        {
            throw new ArgumentException("An absolute SFTP path is required.", nameof(path));
        }

        normalized = "/" + string.Join(
            '/',
            normalized.Split('/', StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length == 1 ? "/" : normalized;
    }
}
