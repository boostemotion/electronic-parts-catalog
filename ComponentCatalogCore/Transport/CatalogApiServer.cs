using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Contracts;

namespace ComponentCatalogCore.Transport;

public sealed class CatalogApiServer : IAsyncDisposable
{
    public event EventHandler? CatalogChanged;

    private readonly CatalogRepository _repository;
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly Task _backgroundLoop;

    private CatalogApiServer(CatalogRepository repository, HttpListener listener, string baseUrl)
    {
        _repository = repository;
        _listener = listener;
        BaseUrl = baseUrl.TrimEnd('/');
        _backgroundLoop = Task.Run(ProcessLoopAsync);
    }

    public string BaseUrl { get; }

    public static Task<CatalogApiServer> StartAsync(CatalogRepository repository, int port = 0)
    {
        var selectedPort = port == 0 ? FindAvailablePort() : port;
        var baseUrl = $"http://127.0.0.1:{selectedPort}";
        var listener = new HttpListener();
        listener.Prefixes.Add($"{baseUrl}/");
        listener.Start();

        return Task.FromResult(new CatalogApiServer(repository, listener, baseUrl));
    }

    public async ValueTask DisposeAsync()
    {
        _cancellation.Cancel();
        _listener.Stop();
        _listener.Close();

        try
        {
            await _backgroundLoop;
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
    }

    private async Task ProcessLoopAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            HttpListenerContext? context = null;
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

            if (context is null)
            {
                continue;
            }

            await HandleRequestAsync(context);
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;
        var url = request.Url;

        if (url is null)
        {
            await WriteJsonAsync(response, HttpStatusCode.BadRequest, new { error = "Missing request url" });
            return;
        }

        if (request.HttpMethod == "OPTIONS")
        {
            WriteCorsPreflight(response);
            return;
        }

        // POST / PUT: 新增或修改元件 (AI 可读写 API)
        if ((request.HttpMethod == "POST" || request.HttpMethod == "PUT") && url.AbsolutePath == "/components")
        {
            try
            {
                using var reader = new System.IO.StreamReader(request.InputStream, Encoding.UTF8);
                var json = await reader.ReadToEndAsync();
                var component = JsonSerializer.Deserialize<ComponentRecord>(json, CatalogJson.DefaultOptions);
                if (component is null)
                {
                    await WriteJsonAsync(response, HttpStatusCode.BadRequest, new { error = "Invalid JSON component body" });
                    return;
                }

                if (string.IsNullOrWhiteSpace(component.Id))
                {
                    var seed = !string.IsNullOrWhiteSpace(component.PartNumber) ? component.PartNumber : component.Name;
                    var normalized = (seed ?? string.Empty).Trim().ToLowerInvariant().Replace(" ", "-");
                    var candidate = string.IsNullOrWhiteSpace(normalized) ? $"component-{Guid.NewGuid():N}" : normalized;

                    var existing = await _repository.GetByIdAsync(candidate);
                    if (existing != null)
                    {
                        candidate = $"{candidate}-{Guid.NewGuid().ToString("N")[..6]}";
                    }
                    component.Id = candidate;
                }

                await _repository.UpsertComponentAsync(component);
                CatalogChanged?.Invoke(this, EventArgs.Empty);

                await WriteJsonAsync(response, HttpStatusCode.OK, new { success = true, component });
            }
            catch (Exception ex)
            {
                await WriteJsonAsync(response, HttpStatusCode.InternalServerError, new { error = ex.Message });
            }
            return;
        }

        // DELETE: 删除元件 (AI 可读写 API)
        if (request.HttpMethod == "DELETE" && url.AbsolutePath.StartsWith("/components/", StringComparison.Ordinal))
        {
            try
            {
                var id = Uri.UnescapeDataString(url.AbsolutePath["/components/".Length..]);
                var existing = await _repository.GetByIdAsync(id);
                if (existing is null)
                {
                    await WriteJsonAsync(response, HttpStatusCode.NotFound, new { error = "Component not found" });
                    return;
                }

                await _repository.DeleteByIdAsync(id);
                CatalogChanged?.Invoke(this, EventArgs.Empty);

                await WriteJsonAsync(response, HttpStatusCode.OK, new { success = true, message = $"Deleted component {id}" });
            }
            catch (Exception ex)
            {
                await WriteJsonAsync(response, HttpStatusCode.InternalServerError, new { error = ex.Message });
            }
            return;
        }

        if (request.HttpMethod == "GET" && url.AbsolutePath == "/categories")
        {
            await WriteJsonAsync(response, HttpStatusCode.OK, new
            {
                categories = await _repository.ListCategoriesAsync(),
            });
            return;
        }

        if (request.HttpMethod == "GET" && (url.AbsolutePath == "/components/search" || url.AbsolutePath == "/components"))
        {
            var query = new SearchQuery
            {
                Text = request.QueryString["q"] ?? string.Empty,
                Category = request.QueryString["category"] ?? "全部",
                Package = string.IsNullOrWhiteSpace(request.QueryString["package"]) ? null : request.QueryString["package"],
                Owned = bool.TryParse(request.QueryString["owned"], out var ownedValue) ? ownedValue : null,
                AttributeText = string.IsNullOrWhiteSpace(request.QueryString["attribute"]) ? null : request.QueryString["attribute"],
            };

            await WriteJsonAsync(response, HttpStatusCode.OK, new
            {
                results = await _repository.SearchAsync(query),
            });
            return;
        }

        if (request.HttpMethod == "GET" && url.AbsolutePath.StartsWith("/components/", StringComparison.Ordinal))
        {
            var id = Uri.UnescapeDataString(url.AbsolutePath["/components/".Length..]);
            var component = await _repository.GetByIdAsync(id);
            if (component is null)
            {
                await WriteJsonAsync(response, HttpStatusCode.NotFound, new { error = "Component not found" });
                return;
            }

            await WriteJsonAsync(response, HttpStatusCode.OK, new { component });
            return;
        }

        await WriteJsonAsync(response, HttpStatusCode.NotFound, new { error = "Not found" });
    }

    private static async Task WriteJsonAsync(HttpListenerResponse response, HttpStatusCode statusCode, object body)
    {
        var payload = JsonSerializer.Serialize(body, CatalogJson.DefaultOptions);
        var bytes = Encoding.UTF8.GetBytes(payload);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json; charset=utf-8";
        response.ContentEncoding = Encoding.UTF8;
        response.ContentLength64 = bytes.Length;
        AddCorsHeaders(response);
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static void WriteCorsPreflight(HttpListenerResponse response)
    {
        response.StatusCode = (int)HttpStatusCode.NoContent;
        response.ContentLength64 = 0;
        AddCorsHeaders(response);
        response.Close();
    }

    private static void AddCorsHeaders(HttpListenerResponse response)
    {
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type";
    }

    private static int FindAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
