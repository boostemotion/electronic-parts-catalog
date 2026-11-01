using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Export;
using ComponentCatalogCore.Importing;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Taxonomy;
using ComponentCatalogCore.Validation;

namespace ComponentCatalogCore.Transport;

public sealed class CatalogApiServer : IAsyncDisposable
{
    public const int MaximumManagementRequestBodyBytes = 1_048_576;
    public const int MaximumRequestBodyBytes = MaximumManagementRequestBodyBytes;
    public const int MaximumConcurrentApkDownloads = 2;

    public event EventHandler? CatalogChanged;

    private readonly CatalogRepository _repository;
    private readonly BomImportPreviewStore _previewStore = new();
    private readonly BomImportPreviewService _previewService;
    private readonly HttpListener _listener;
    private readonly CatalogApiBindingOptions _bindingOptions;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _backgroundLoop;
    private readonly byte[] _bearerTokenBytes;
    private readonly HashSet<string> _allowedOrigins;
    private readonly AndroidAppRelease? _androidAppRelease;
    private readonly SemaphoreSlim _apkDownloadSlots = new(MaximumConcurrentApkDownloads, MaximumConcurrentApkDownloads);
    private readonly ConcurrentDictionary<long, Task> _activeRequests = new();
    private long _nextRequestId;
    private bool _disposed;

    private CatalogApiServer(
        CatalogRepository repository,
        HttpListener listener,
        CatalogApiBindingOptions bindingOptions,
        string bearerToken,
        IEnumerable<string>? allowedOrigins,
        AndroidAppRelease? androidAppRelease)
    {
        _repository = repository;
        _previewService = new BomImportPreviewService(_repository, _previewStore);
        _listener = listener;
        _bindingOptions = bindingOptions;
        BaseUrl = bindingOptions.BaseUrl;
        BaseUrls = bindingOptions.BaseUrls;
        _bearerTokenBytes = Encoding.UTF8.GetBytes(bearerToken);
        _allowedOrigins = new HashSet<string>(allowedOrigins ?? [], StringComparer.Ordinal);
        _androidAppRelease = androidAppRelease;
        _backgroundLoop = Task.Run(ProcessLoopAsync);
    }

    public string BaseUrl { get; }

    public IReadOnlyList<string> BaseUrls { get; }

    public static Task<CatalogApiServer> StartAsync(
        CatalogRepository repository,
        string bearerToken,
        int port = 0,
        IEnumerable<string>? allowedOrigins = null,
        AndroidAppRelease? androidAppRelease = null)
    {
        var selectedPort = port == 0 ? FindAvailablePort() : port;
        return StartAsync(repository, bearerToken, new CatalogApiBindingOptions(selectedPort), allowedOrigins, androidAppRelease);
    }

    public static Task<CatalogApiServer> StartAsync(
        CatalogRepository repository,
        string bearerToken,
        CatalogApiBindingOptions bindingOptions,
        IEnumerable<string>? allowedOrigins = null,
        AndroidAppRelease? androidAppRelease = null)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentException.ThrowIfNullOrWhiteSpace(bearerToken);
        ArgumentNullException.ThrowIfNull(bindingOptions);

        var listener = new HttpListener();
        foreach (var prefix in bindingOptions.Prefixes)
        {
            listener.Prefixes.Add(prefix);
        }

        try
        {
            listener.Start();
        }
        catch (HttpListenerException exception)
        {
            listener.Close();
            var error = exception.NativeErrorCode == 5
                ? "访问被拒绝。请以管理员身份为每个明确 URL 配置 URL ACL，或以管理员身份启动应用。"
                : "端口已被占用，或无法绑定所选地址。";
            throw new InvalidOperationException($"无法启动本地 API：{string.Join("、", bindingOptions.BaseUrls)}。{error}", exception);
        }

        return Task.FromResult(new CatalogApiServer(repository, listener, bindingOptions, bearerToken, allowedOrigins, androidAppRelease));
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cancellation.Cancel();
        _listener.Stop();
        _listener.Close();

        try
        {
            await _backgroundLoop;
            await Task.WhenAll(_activeRequests.Values);
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
        finally
        {
            _apkDownloadSlots.Dispose();
            _cancellation.Dispose();
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            HttpListenerContext? context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (ObjectDisposedException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException) when (_cancellation.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                continue;
            }

            TrackRequest(context);
        }
    }

    private void TrackRequest(HttpListenerContext context)
    {
        var requestId = Interlocked.Increment(ref _nextRequestId);
        var requestTask = ProcessRequestAsync(context);
        _activeRequests.TryAdd(requestId, requestTask);
        _ = ObserveRequestAsync(requestId, requestTask);
    }

    private async Task ObserveRequestAsync(long requestId, Task requestTask)
    {
        try
        {
            await requestTask;
        }
        finally
        {
            _activeRequests.TryRemove(requestId, out _);
        }
    }

    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            await HandleRequestAsync(context);
        }
        catch (Exception)
        {
            await TryWriteUnexpectedErrorAsync(context);
        }
        finally
        {
            TryCloseResponse(context.Response);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var url = request.Url;

        if (url is null)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "Missing request url" });
            return;
        }

        if (IsLanRequest(request))
        {
            if (url.AbsolutePath is "/mobile/snapshot" or AndroidAppRelease.ManifestPath or AndroidAppRelease.ApkPath)
            {
                await HandleMobileRequestAsync(request, response, url);
                return;
            }

            // The LAN listener deliberately exposes no management surface. Preserve Bearer
            // authentication for every management route before returning its LAN-only 404.
            if (!IsAuthorized(request))
            {
                await WriteJsonAsync(request, response, HttpStatusCode.Unauthorized, new { error = "Authorization is required." });
                return;
            }

            await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Not found" });
            return;
        }

        if (request.HttpMethod == "OPTIONS")
        {
            WriteCorsPreflight(request, response);
            return;
        }

        if (!IsAuthorized(request))
        {
            await WriteJsonAsync(request, response, HttpStatusCode.Unauthorized, new { error = "Authorization is required." });
            return;
        }

        if (request.HttpMethod == "POST" && url.AbsolutePath == "/imports/bom/preview")
        {
            await HandleBomPreviewAsync(request, response);
            return;
        }

        if (request.HttpMethod == "POST" && url.AbsolutePath == "/imports/bom/commit")
        {
            await HandleBomCommitAsync(request, response);
            return;
        }

        if ((request.HttpMethod == "POST" || request.HttpMethod == "PUT") && url.AbsolutePath == "/components")
        {
            await HandleSaveAsync(request, response);
            return;
        }

        if (request.HttpMethod == "DELETE" && url.AbsolutePath.StartsWith("/components/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(url.AbsolutePath["/components/".Length..]);
            var existing = await _repository.GetByIdAsync(id);
            if (existing is null)
            {
                await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Component not found" });
                return;
            }

            await _repository.DeleteByIdAsync(id);
            CatalogChanged?.Invoke(this, EventArgs.Empty);
            await WriteJsonAsync(request, response, HttpStatusCode.OK, new { success = true, message = $"Deleted component {id}" });
            return;
        }

        if (request.HttpMethod == "GET" && url.AbsolutePath == "/taxonomy/categories")
        {
            await WriteJsonAsync(request, response, HttpStatusCode.OK, ComponentCategoryTaxonomy.Definition);
            return;
        }

        if (request.HttpMethod == "GET" && url.AbsolutePath == "/categories")
        {
            await WriteJsonAsync(request, response, HttpStatusCode.OK, new { categories = await _repository.ListCategoriesAsync() });
            return;
        }

        if (request.HttpMethod == "GET" && (url.AbsolutePath == "/components/search" || url.AbsolutePath == "/components"))
        {
            var query = new SearchQuery
            {
                Text = request.QueryString["q"] ?? string.Empty,
                Category = request.QueryString["category"] ?? "全部",
                MajorCategoryId = string.IsNullOrWhiteSpace(request.QueryString["majorCategoryId"]) ? null : request.QueryString["majorCategoryId"],
                SubCategoryId = string.IsNullOrWhiteSpace(request.QueryString["subCategoryId"]) ? null : request.QueryString["subCategoryId"],
                Package = string.IsNullOrWhiteSpace(request.QueryString["package"]) ? null : request.QueryString["package"],
                Owned = bool.TryParse(request.QueryString["owned"], out var ownedValue) ? ownedValue : null,
                AttributeText = string.IsNullOrWhiteSpace(request.QueryString["attribute"]) ? null : request.QueryString["attribute"],
            };

            await WriteJsonAsync(request, response, HttpStatusCode.OK, new { results = await _repository.SearchAsync(query) });
            return;
        }

        if (request.HttpMethod == "GET" && url.AbsolutePath.StartsWith("/components/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(url.AbsolutePath["/components/".Length..]);
            var component = await _repository.GetByIdAsync(id);
            if (component is null)
            {
                await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Component not found" });
                return;
            }

            await WriteJsonAsync(request, response, HttpStatusCode.OK, new { component });
            return;
        }

        await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Not found" });
    }

    private async Task HandleMobileRequestAsync(HttpListenerRequest request, HttpListenerResponse response, Uri url)
    {
        AddMobileHeaders(response);

        if (request.HttpMethod == "OPTIONS")
        {
            response.StatusCode = (int)HttpStatusCode.NoContent;
            response.ContentLength64 = 0;
            return;
        }

        if (request.HttpMethod is not ("GET" or "HEAD"))
        {
            response.Headers["Allow"] = "GET, HEAD, OPTIONS";
            await WriteJsonAsync(request, response, HttpStatusCode.MethodNotAllowed, new { error = "Mobile endpoint is read-only." });
            return;
        }

        if (!string.IsNullOrEmpty(url.Query))
        {
            await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Not found" });
            return;
        }

        if (url.AbsolutePath == "/mobile/snapshot")
        {
            if (request.HttpMethod == "HEAD")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.ContentLength64 = 0;
                return;
            }

            var snapshot = CatalogSnapshotService.BuildSnapshot(
                await _repository.ListComponentsAsync(),
                DateTimeOffset.UtcNow.ToString("O"));
            await WriteJsonAsync(request, response, HttpStatusCode.OK, snapshot);
            return;
        }

        if (url.AbsolutePath == AndroidAppRelease.ManifestPath)
        {
            var manifest = _androidAppRelease is null ? null : await _androidAppRelease.TryGetManifestAsync(_cancellation.Token);
            if (manifest is null)
            {
                await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Android app release is unavailable." });
                return;
            }

            await WriteJsonAsync(request, response, HttpStatusCode.OK, manifest);
            return;
        }

        if (url.AbsolutePath == AndroidAppRelease.ApkPath)
        {
            await HandleAndroidAppApkAsync(request, response);
            return;
        }

        await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Not found" });
    }

    private async Task HandleAndroidAppApkAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        var manifest = _androidAppRelease is null ? null : await _androidAppRelease.TryGetManifestAsync(_cancellation.Token);
        if (manifest is null || _androidAppRelease is null)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "Android app release is unavailable." });
            return;
        }

        response.StatusCode = (int)HttpStatusCode.OK;
        response.ContentType = "application/vnd.android.package-archive";
        response.ContentLength64 = manifest.SizeBytes;
        response.Headers["ETag"] = $"\"{manifest.Sha256}\"";
        if (request.HttpMethod == "HEAD")
        {
            return;
        }

        await _apkDownloadSlots.WaitAsync(_cancellation.Token);
        try
        {
            await using var apkStream = new FileStream(_androidAppRelease.SourceApkPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
            await apkStream.CopyToAsync(response.OutputStream, _cancellation.Token);
        }
        finally
        {
            _apkDownloadSlots.Release();
        }
    }

    private bool IsLanRequest(HttpListenerRequest request)
    {
        return _bindingOptions.LanIpv4 is not null &&
            string.Equals(request.LocalEndPoint?.Address.ToString(), _bindingOptions.LanIpv4, StringComparison.Ordinal);
    }

    private static void AddMobileHeaders(HttpListenerResponse response)
    {
        response.Headers["Cache-Control"] = "no-store";
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, HEAD, OPTIONS";
    }

    private async Task HandleBomPreviewAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.ContentLength64 < 0 || request.ContentLength64 > MaximumRequestBodyBytes)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.RequestEntityTooLarge, new { error = "Request body is too large." });
            return;
        }

        BomImportPreviewRequest? previewRequest;
        try
        {
            previewRequest = JsonSerializer.Deserialize<BomImportPreviewRequest>(await ReadBodyAsync(request, _cancellation.Token), CatalogJson.DefaultOptions);
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.RequestEntityTooLarge, new { error = "Request body is too large." });
            return;
        }
        catch (JsonException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "Invalid JSON BOM import body." });
            return;
        }

        if (previewRequest is null || previewRequest.Rows.Count == 0)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "At least one BOM row is required." });
            return;
        }

        await WriteJsonAsync(request, response, HttpStatusCode.OK, await _previewService.CreatePreviewAsync(previewRequest));
    }

    private async Task HandleBomCommitAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.ContentLength64 < 0 || request.ContentLength64 > MaximumRequestBodyBytes)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.RequestEntityTooLarge, new { error = "Request body is too large." });
            return;
        }

        BomImportCommitRequest? commitRequest;
        try
        {
            commitRequest = JsonSerializer.Deserialize<BomImportCommitRequest>(await ReadBodyAsync(request, _cancellation.Token), CatalogJson.DefaultOptions);
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.RequestEntityTooLarge, new { error = "Request body is too large." });
            return;
        }
        catch (JsonException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "Invalid JSON BOM commit body." });
            return;
        }

        if (commitRequest is null || string.IsNullOrWhiteSpace(commitRequest.PreviewId) || commitRequest.RowIndexes.Count == 0 ||
            commitRequest.RowIndexes.Distinct().Count() != commitRequest.RowIndexes.Count)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "A preview id and unique row indexes are required." });
            return;
        }

        var preview = _previewStore.Get(commitRequest.PreviewId);
        if (preview is null)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.NotFound, new { error = "BOM preview not found or expired." });
            return;
        }

        var selected = commitRequest.RowIndexes
            .Select(index => preview.Items.SingleOrDefault(item => item.RowIndex == index))
            .ToList();
        if (selected.Any(item => item is null))
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "Selected row is not present in the preview." });
            return;
        }

        if (selected.Any(item => !BomImportPreviewService.CanCommit(item!)))
        {
            await WriteJsonAsync(request, response, HttpStatusCode.Conflict, new { error = "Rows requiring review or containing warnings cannot be committed." });
            return;
        }

        try
        {
            await _repository.SaveComponentsAsync(selected.Select(item => item!.CommitRecord).ToList());
        }
        catch (DuplicatePartNumberException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.Conflict, new { error = "PartNumber and Package are already used by another component." });
            return;
        }

        CatalogChanged?.Invoke(this, EventArgs.Empty);
        await WriteJsonAsync(request, response, HttpStatusCode.OK, new { success = true, count = selected.Count });
    }

    private async Task HandleSaveAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.ContentLength64 < 0 || request.ContentLength64 > MaximumRequestBodyBytes)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.RequestEntityTooLarge, new { error = "Request body is too large." });
            return;
        }

        ComponentRecord? component;
        try
        {
            component = JsonSerializer.Deserialize<ComponentRecord>(await ReadBodyAsync(request, _cancellation.Token), CatalogJson.DefaultOptions);
        }
        catch (RequestBodyTooLargeException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.RequestEntityTooLarge, new { error = "Request body is too large." });
            return;
        }
        catch (JsonException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "Invalid JSON component body." });
            return;
        }

        if (component is null)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = "Invalid JSON component body." });
            return;
        }

        var validationError = ComponentRecordValidator.ValidateAndNormalize(component);
        if (validationError is not null)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.BadRequest, new { error = validationError });
            return;
        }

        if (string.IsNullOrWhiteSpace(component.Id))
        {
            component.Id = CreateGeneratedId(component.PartNumber);
        }

        try
        {
            await _repository.SaveComponentAsync(component);
        }
        catch (DuplicatePartNumberException)
        {
            await WriteJsonAsync(request, response, HttpStatusCode.Conflict, new { error = "PartNumber and Package are already used by another component." });
            return;
        }

        CatalogChanged?.Invoke(this, EventArgs.Empty);
        await WriteJsonAsync(request, response, HttpStatusCode.OK, new { success = true, component });
    }

    private static async Task<string> ReadBodyAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        using var stream = request.InputStream;
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        var total = 0;
        while (true)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(), cancellationToken);
            if (read == 0)
            {
                return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, checked((int)buffer.Length));
            }

            total += read;
            if (total > MaximumRequestBodyBytes)
            {
                throw new RequestBodyTooLargeException();
            }

            await buffer.WriteAsync(bytes.AsMemory(0, read));
        }
    }

    private bool IsAuthorized(HttpListenerRequest request)
    {
        var header = request.Headers["Authorization"];
        const string prefix = "Bearer ";
        if (header is null || !header.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var supplied = Encoding.UTF8.GetBytes(header[prefix.Length..]);
        return supplied.Length == _bearerTokenBytes.Length && CryptographicOperations.FixedTimeEquals(supplied, _bearerTokenBytes);
    }

    private async Task TryWriteUnexpectedErrorAsync(HttpListenerContext context)
    {
        try
        {
            await WriteJsonAsync(context.Request, context.Response, HttpStatusCode.InternalServerError, new { error = "Unexpected server error." });
        }
        catch
        {
            // A disconnected client or a failed response write must not stop the accept loop.
        }
    }

    private async Task WriteJsonAsync(HttpListenerRequest request, HttpListenerResponse response, HttpStatusCode statusCode, object body)
    {
        var payload = JsonSerializer.Serialize(body, CatalogJson.DefaultOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        AddCorsHeaders(request, response);
        if (request.HttpMethod != "HEAD")
        {
            await response.OutputStream.WriteAsync(bytes);
        }
    }

    private void WriteCorsPreflight(HttpListenerRequest request, HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NoContent;
        response.ContentLength64 = 0;
        AddCorsHeaders(request, response);
    }

    private void AddCorsHeaders(HttpListenerRequest request, HttpListenerResponse response)
    {
        var origin = request.Headers["Origin"];
        if (origin is null || !_allowedOrigins.Contains(origin))
        {
            return;
        }

        response.Headers["Access-Control-Allow-Origin"] = origin;
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Authorization, Content-Type";
        response.Headers["Vary"] = "Origin";
    }

    private static string CreateGeneratedId(string partNumber)
    {
        var normalized = partNumber.Trim().ToLowerInvariant().Replace(" ", "-");
        var prefix = string.IsNullOrWhiteSpace(normalized) ? "component" : normalized;
        return $"{prefix}-{Guid.NewGuid():N}";
    }

    private static void TryCloseResponse(HttpListenerResponse response)
    {
        try
        {
            response.Close();
        }
        catch
        {
            // Closing an already aborted response is expected for disconnected clients.
        }
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private sealed class RequestBodyTooLargeException : Exception;
}
