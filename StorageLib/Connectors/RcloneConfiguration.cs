using System.Text.Json;

namespace StorageLib.Connectors;

internal static class RcloneConfiguration
{
    private const int MinimumStartupTimeoutSeconds = 1;
    private const int MaximumStartupTimeoutSeconds = 300;

    public static RcloneConnectionOptions Parse(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Rclone connection JSON is required.", nameof(connectionString));
        }

        try
        {
            return JsonSerializer.Deserialize<RcloneConnectionOptions>(
                connectionString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new ArgumentException("Rclone connection JSON cannot be null.", nameof(connectionString));
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Rclone connection JSON is invalid.", nameof(connectionString), exception);
        }
    }

    public static void Validate(RcloneConnectionOptions options)
    {
        ValidateRemoteName(options.RemoteName);
        ValidateExistingFile(options.ConfigPath, nameof(options.ConfigPath));
        ValidateExistingFile(options.ExecutablePath, nameof(options.ExecutablePath));
        if (options.StartupTimeoutSeconds is < MinimumStartupTimeoutSeconds or > MaximumStartupTimeoutSeconds)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.StartupTimeoutSeconds),
                $"Startup timeout must be between {MinimumStartupTimeoutSeconds} and {MaximumStartupTimeoutSeconds} seconds.");
        }
    }

    public static string BuildTarget(string remoteName, string? subpath)
    {
        ValidateRemoteName(remoteName);
        string normalized = NormalizeSubpath(subpath);
        return normalized.Length == 0 ? $"{remoteName}:" : $"{remoteName}:{normalized}";
    }

    public static string NormalizeSubpath(string? subpath)
    {
        if (string.IsNullOrWhiteSpace(subpath) || subpath == "/")
        {
            return string.Empty;
        }

        string normalized = subpath.Replace('\\', '/');
        string[] segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (normalized.Contains('\0') ||
            normalized.Contains(':') ||
            segments.Any(segment => segment is "." or ".."))
        {
            throw new ArgumentException("The rclone subpath must be relative and cannot contain traversal.", nameof(subpath));
        }

        return string.Join('/', segments);
    }

    public static bool IsOwnableConfigPath(string path) =>
        !string.IsNullOrWhiteSpace(path) &&
        Path.IsPathFullyQualified(path) &&
        File.Exists(path);

    private static void ValidateExistingFile(string path, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("An absolute file path is required.", parameterName);
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The configured file does not exist.", path);
        }
    }

    private static void ValidateRemoteName(string remoteName)
    {
        if (string.IsNullOrWhiteSpace(remoteName) ||
            remoteName.Any(character =>
                !(char.IsLetterOrDigit(character) || character is '_' or '-' or '.' or ' ')))
        {
            throw new ArgumentException("The rclone remote name contains invalid characters.", nameof(remoteName));
        }
    }
}
