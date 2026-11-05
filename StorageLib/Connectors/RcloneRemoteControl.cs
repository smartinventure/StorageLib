using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using StorageLib.Abstractions;

namespace StorageLib.Connectors;

/// <summary>
/// Talks to the control interface of one rclone process over loopback. It exists for a single question —
/// how much is still waiting to be uploaded — which a shutdown has to ask before it stops the process and
/// a mount wants to show while it runs.
/// </summary>
internal sealed class RcloneRemoteControl : IDisposable
{
    // One client for the lifetime of the connection: a client per request exhausts sockets, which is the
    // reason handlers are meant to be shared. This one is private to a single loopback endpoint.
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly Uri _endpoint;
    private readonly string _authentication;
    private bool _isDisposed;

    public RcloneRemoteControl(int port, string username, string password)
    {
        _endpoint = new Uri($"http://127.0.0.1:{port}/");
        _authentication = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
    }

    public int Port => _endpoint.Port;

    /// <summary>
    /// Asks rclone's virtual filesystem what it still holds. Returns null when rclone cannot be reached or
    /// answers with something unexpected, which the caller has to treat as "cannot tell" rather than "nothing".
    /// </summary>
    public async Task<PendingUploadState?> GetPendingUploadsAsync(CancellationToken cancellationToken)
    {
        string? payload = await PostAsync("vfs/stats", cancellationToken).ConfigureAwait(false);
        if (payload is null)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("diskCache", out var diskCache))
            {
                // No disk cache in this mode means nothing can be waiting: the write went straight out.
                return PendingUploadState.None;
            }

            int queued = ReadCount(diskCache, "uploadsQueued");
            int uploading = ReadCount(diskCache, "uploadsInProgress");
            int errored = ReadCount(diskCache, "erroredFiles");
            return new PendingUploadState(queued + uploading, 0, errored);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        _client.Dispose();
    }

    private static int ReadCount(JsonElement parent, string propertyName) =>
        parent.TryGetProperty(propertyName, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out int count)
            ? count
            : 0;

    private async Task<string?> PostAsync(string command, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_endpoint, command));
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", _authentication);
            using var response = await _client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException or ObjectDisposedException)
        {
            return null;
        }
    }
}
