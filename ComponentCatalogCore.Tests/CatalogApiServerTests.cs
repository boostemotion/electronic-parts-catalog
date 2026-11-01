using System.Net;
using System.Net.Http.Headers;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ComponentCatalogCore.Contracts;
using ComponentCatalogCore.Persistence;
using ComponentCatalogCore.Transport;

namespace ComponentCatalogCore.Tests;

public sealed class CatalogApiServerTests : IAsyncDisposable
{
    private const string Token = "test-token-for-local-api-regression-tests";
    private readonly string _workingDirectory = Path.Combine(Path.GetTempPath(), $"catalog-api-tests-{Guid.NewGuid():N}");
    private readonly List<IAsyncDisposable> _asyncDisposables = [];

    [Fact]
    public void ApiServer_DeclaresBoundedManagementBodiesAndApkDownloads()
    {
        Assert.Equal(1_048_576, CatalogApiServer.MaximumManagementRequestBodyBytes);
        Assert.Equal(2, CatalogApiServer.MaximumConcurrentApkDownloads);
    }

    [Fact]
    public async Task ApiServer_UsesExplicitBindingOptionsForLoopback()
    {
        var repository = await CreateRepositoryAsync();
        var options = new CatalogApiBindingOptions(GetAvailablePort());

        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token, options);

        Assert.Equal(options.BaseUrl, apiServer.BaseUrl);
        Assert.Equal(options.BaseUrls, apiServer.BaseUrls);
    }

    [Fact]
    public async Task ApiServer_ReleasesLoopbackPortOnDispose()
    {
        var repository = await CreateRepositoryAsync();
        var port = GetAvailablePort();

        await using (var apiServer = await CatalogApiServer.StartAsync(repository, Token, new CatalogApiBindingOptions(port)))
        {
            Assert.Equal($"http://127.0.0.1:{port}", apiServer.BaseUrl);
        }

        await using var replacement = await CatalogApiServer.StartAsync(repository, Token, new CatalogApiBindingOptions(port));
        Assert.Equal($"http://127.0.0.1:{port}", replacement.BaseUrl);
    }

    [Fact]
    public async Task ApiServer_RequiresAuthorizationForReadEndpoints()
    {
        var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("mcu-stm32", "STM32F103C8T6"));
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token);
        using var client = new HttpClient();

        using var unauthorized = await client.GetAsync($"{apiServer.BaseUrl}/categories");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var categoriesResponse = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrl}/categories"));
        categoriesResponse.EnsureSuccessStatusCode();
        using var categoriesDoc = JsonDocument.Parse(await categoriesResponse.Content.ReadAsStringAsync());
        var categories = categoriesDoc.RootElement.GetProperty("categories").EnumerateArray().Select(item => item.GetString() ?? string.Empty).ToArray();
        Assert.Equal("全部", categories[0]);
        Assert.Contains("微控制器 / 逻辑器件", categories);

        using var searchResponse = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrl}/components/search?q=STM32F103C8T6"));
        searchResponse.EnsureSuccessStatusCode();
        using var searchDoc = JsonDocument.Parse(await searchResponse.Content.ReadAsStringAsync());
        Assert.Equal("mcu-stm32", Assert.Single(searchDoc.RootElement.GetProperty("results").EnumerateArray()).GetProperty("component").GetProperty("id").GetString());

        using var detailResponse = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrl}/components/mcu-stm32"));
        detailResponse.EnsureSuccessStatusCode();
        using var detailDoc = JsonDocument.Parse(await detailResponse.Content.ReadAsStringAsync());
        Assert.Equal("STM32F103C8T6", detailDoc.RootElement.GetProperty("component").GetProperty("partNumber").GetString());
    }

    [Fact]
    public async Task ApiServer_ValidatesWritesAuthenticationDuplicateConflictsAndCors()
    {
        var repository = await CreateRepositoryAsync();
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token, allowedOrigins: ["https://catalog.example.test"]);
        using var client = new HttpClient();
        var component = CreateComponent("api-ldo", "API-LDO-33");
        var payload = JsonSerializer.Serialize(component, CatalogJson.DefaultOptions);

        using var noToken = await client.PostAsync($"{apiServer.BaseUrl}/components", new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, noToken.StatusCode);

        using var wrongTokenRequest = new HttpRequestMessage(HttpMethod.Post, $"{apiServer.BaseUrl}/components")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
        };
        wrongTokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
        using var wrongToken = await client.SendAsync(wrongTokenRequest);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);

        using var malformed = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent("{", Encoding.UTF8, "application/json"));
        using var malformedResponse = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);

        using var missingFields = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent("{}", Encoding.UTF8, "application/json"));
        using var missingFieldsResponse = await client.SendAsync(missingFields);
        Assert.Equal(HttpStatusCode.BadRequest, missingFieldsResponse.StatusCode);

        var oversizedContent = new ByteArrayContent(new byte[CatalogApiServer.MaximumRequestBodyBytes + 1]);
        using var oversized = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", oversizedContent);
        using var oversizedResponse = await client.SendAsync(oversized);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);

        using var create = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent(payload, Encoding.UTF8, "application/json"));
        using var createResponse = await client.SendAsync(create);
        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        Assert.False(createResponse.Headers.Contains("Access-Control-Allow-Origin"));

        var duplicate = CreateComponent("other-id", "API-LDO-33");
        using var duplicateRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent(JsonSerializer.Serialize(duplicate, CatalogJson.DefaultOptions), Encoding.UTF8, "application/json"));
        using var duplicateResponse = await client.SendAsync(duplicateRequest);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);

        using var allowedOrigin = CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrl}/components/api-ldo");
        allowedOrigin.Headers.TryAddWithoutValidation("Origin", "https://catalog.example.test");
        using var allowedOriginResponse = await client.SendAsync(allowedOrigin);
        Assert.Equal("https://catalog.example.test", allowedOriginResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Contains("Authorization", allowedOriginResponse.Headers.GetValues("Access-Control-Allow-Headers").Single());

        using var preflight = new HttpRequestMessage(HttpMethod.Options, $"{apiServer.BaseUrl}/components");
        preflight.Headers.TryAddWithoutValidation("Origin", "https://catalog.example.test");
        using var preflightResponse = await client.SendAsync(preflight);
        Assert.Equal(HttpStatusCode.NoContent, preflightResponse.StatusCode);
        Assert.Equal("https://catalog.example.test", preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task ApiServer_UsesPartNumberAndPackageAsTheDuplicateIdentity()
    {
        var repository = await CreateRepositoryAsync();
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token);
        using var client = new HttpClient();
        var resistor0603 = CreateComponent("api-resistor-0603", "10k", package: "0603");
        var resistor0805 = CreateComponent("api-resistor-0805", "10k", package: "0805");
        var duplicate0805 = CreateComponent("api-other-resistor-0805", "10k", package: "0805");

        using var create0603 = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent(JsonSerializer.Serialize(resistor0603, CatalogJson.DefaultOptions), Encoding.UTF8, "application/json"));
        using var create0603Response = await client.SendAsync(create0603);
        Assert.Equal(HttpStatusCode.OK, create0603Response.StatusCode);

        using var create0805 = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent(JsonSerializer.Serialize(resistor0805, CatalogJson.DefaultOptions), Encoding.UTF8, "application/json"));
        using var create0805Response = await client.SendAsync(create0805);
        Assert.Equal(HttpStatusCode.OK, create0805Response.StatusCode);

        using var duplicate = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent(JsonSerializer.Serialize(duplicate0805, CatalogJson.DefaultOptions), Encoding.UTF8, "application/json"));
        using var duplicateResponse = await client.SendAsync(duplicate);
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Contains("PartNumber and Package", await duplicateResponse.Content.ReadAsStringAsync());
        Assert.Equal(2, (await repository.ListComponentsAsync()).Count);
    }

    [Fact]
    public async Task ApiServer_PortCollisionDoesNotPreventRepositoryUse()
    {
        var repository = await CreateRepositoryAsync();
        using var occupiedListener = new TcpListener(IPAddress.Loopback, 0);
        occupiedListener.Start();
        var port = ((IPEndPoint)occupiedListener.LocalEndpoint).Port;

        await Assert.ThrowsAsync<InvalidOperationException>(() => CatalogApiServer.StartAsync(repository, Token, port));

        await repository.SaveComponentAsync(CreateComponent("repository-only", "REPOSITORY-ONLY"));
        Assert.NotNull(await repository.GetByIdAsync("repository-only"));
    }

    [Fact]
    public async Task ApiServer_ContinuesServingAfterMalformedRequestAndConcurrentRepositoryRead()
    {
        var repository = await CreateRepositoryAsync();
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token);
        using var client = new HttpClient();

        using var malformed = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/components", new StringContent("not-json", Encoding.UTF8, "application/json"));
        using var malformedResponse = await client.SendAsync(malformed);
        Assert.Equal(HttpStatusCode.BadRequest, malformedResponse.StatusCode);

        var writes = Enumerable.Range(0, 8).Select(index =>
        {
            var request = CreateAuthorizedRequest(
                HttpMethod.Post,
                $"{apiServer.BaseUrl}/components",
                new StringContent(JsonSerializer.Serialize(CreateComponent($"api-{index}", $"API-{index}"), CatalogJson.DefaultOptions), Encoding.UTF8, "application/json"));
            return client.SendAsync(request);
        });
        var reads = Enumerable.Range(0, 8).Select(_ => repository.ListComponentsAsync());
        await Task.WhenAll(writes.Cast<Task>().Concat(reads));

        using var getResponse = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrl}/components"));
        getResponse.EnsureSuccessStatusCode();
        Assert.Equal(8, (await repository.ListComponentsAsync()).Count);
    }

    [Fact]
    public async Task ApiServer_ContinuesServingWhenAnAuthorizedRequestBodyStalls()
    {
        var repository = await CreateRepositoryAsync();
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token);
        var port = new Uri(apiServer.BaseUrl).Port;
        using var slowClient = new TcpClient();
        await slowClient.ConnectAsync(IPAddress.Loopback, port);
        await using var slowStream = slowClient.GetStream();
        var partialRequest = $"POST /components HTTP/1.1\r\nHost: 127.0.0.1:{port}\r\nAuthorization: Bearer {Token}\r\nContent-Type: application/json\r\nContent-Length: 100\r\n\r\n{{";
        await slowStream.WriteAsync(Encoding.ASCII.GetBytes(partialRequest));
        await slowStream.FlushAsync();

        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        using var response = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrl}/categories"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task ApiServer_PreviewsAndCommitsCleanBomRowsWithAuthorization()
    {
        var repository = await CreateRepositoryAsync();
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token);
        using var client = new HttpClient();
        var payload = """
        {"rows":[{"quantity":1,"designators":["C1"],"value":"100nf","footprint":"C0603"}]}
        """;

        using var unauthorized = await client.PostAsync($"{apiServer.BaseUrl}/imports/bom/preview", new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        using var previewRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/imports/bom/preview", new StringContent(payload, Encoding.UTF8, "application/json"));
        using var previewResponse = await client.SendAsync(previewRequest);
        previewResponse.EnsureSuccessStatusCode();
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        Assert.Equal("100nF", preview.RootElement.GetProperty("items")[0].GetProperty("record").GetProperty("partNumber").GetString());
        Assert.Equal("0603", preview.RootElement.GetProperty("items")[0].GetProperty("record").GetProperty("package").GetString());
        Assert.Equal("create", preview.RootElement.GetProperty("items")[0].GetProperty("action").GetString());

        var previewId = preview.RootElement.GetProperty("previewId").GetString();
        using var commitRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/imports/bom/commit", new StringContent($"{{\"previewId\":\"{previewId}\",\"rowIndexes\":[0]}}", Encoding.UTF8, "application/json"));
        using var commitResponse = await client.SendAsync(commitRequest);
        commitResponse.EnsureSuccessStatusCode();
        Assert.Equal("100nF", Assert.Single(await repository.ListComponentsAsync()).PartNumber);
    }

    [Fact]
    public async Task ApiServer_BomPreviewAndCommit_CreateSecondPackageForSamePartNumber()
    {
        var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("existing-cap-0603", "100nF", package: "0603"));
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token);
        using var client = new HttpClient();
        const string payload = """
        {"rows":[{"quantity":1,"designators":["C1"],"value":"100nf","footprint":"C0805"}]}
        """;

        using var previewRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/imports/bom/preview", new StringContent(payload, Encoding.UTF8, "application/json"));
        using var previewResponse = await client.SendAsync(previewRequest);
        previewResponse.EnsureSuccessStatusCode();
        using var preview = JsonDocument.Parse(await previewResponse.Content.ReadAsStringAsync());
        Assert.Equal("create", preview.RootElement.GetProperty("items")[0].GetProperty("action").GetString());
        Assert.Equal("100nF", preview.RootElement.GetProperty("items")[0].GetProperty("record").GetProperty("partNumber").GetString());
        Assert.Equal("0805", preview.RootElement.GetProperty("items")[0].GetProperty("record").GetProperty("package").GetString());

        var previewId = preview.RootElement.GetProperty("previewId").GetString();
        using var commitRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/imports/bom/commit", new StringContent($"{{\"previewId\":\"{previewId}\",\"rowIndexes\":[0]}}", Encoding.UTF8, "application/json"));
        using var commitResponse = await client.SendAsync(commitRequest);
        commitResponse.EnsureSuccessStatusCode();

        Assert.Contains(await repository.ListComponentsAsync(), component => component.PartNumber == "100nF" && component.Package == "0603");
        Assert.Contains(await repository.ListComponentsAsync(), component => component.PartNumber == "100nF" && component.Package == "0805");
    }

    [Fact]
    public async Task ApiServer_RejectsUnsafeBomCommitsAndRaisesOneEventForCleanCommit()
    {
        var repository = await CreateRepositoryAsync();
        var existingAms = CreateComponent("ams-existing", "AMS1117-3.3");
        existingAms.Package = "SOT89";
        await repository.SaveComponentAsync(existingAms);
        await using var apiServer = await CatalogApiServer.StartAsync(repository, Token);
        using var client = new HttpClient();
        var eventCount = 0;
        apiServer.CatalogChanged += (_, _) => eventCount++;
        const string previewPayload = """
        {"rows":[
          {"quantity":1,"designators":["C1"],"value":"100nf","footprint":"C0603"},
          {"quantity":1,"designators":["U1"],"manufacturerPart":"AMS1117-3.3","footprint":"SOT223"},
          {"quantity":1,"value":"Unknown part","footprint":"Custom"}
        ]}
        """;

        using var previewRequest = CreateAuthorizedRequest(HttpMethod.Post, $"{apiServer.BaseUrl}/imports/bom/preview", new StringContent(previewPayload, Encoding.UTF8, "application/json"));
        using var previewResponse = await client.SendAsync(previewRequest);
        previewResponse.EnsureSuccessStatusCode();
        var previewJson = await previewResponse.Content.ReadAsStringAsync();
        using var preview = JsonDocument.Parse(previewJson);
        var items = preview.RootElement.GetProperty("items");
        Assert.Equal("create", items[0].GetProperty("action").GetString());
        Assert.Equal("review", items[1].GetProperty("action").GetString());
        Assert.NotEmpty(items[2].GetProperty("warnings").EnumerateArray());
        Assert.DoesNotContain("100nf_C0603", previewJson, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("100nF", items[0].GetProperty("record").GetProperty("name").GetString());
        Assert.Equal("0603", items[0].GetProperty("record").GetProperty("package").GetString());
        var previewId = preview.RootElement.GetProperty("previewId").GetString();

        await AssertCommitStatusAsync(client, apiServer.BaseUrl, $"{{\"previewId\":\"missing-preview\",\"rowIndexes\":[0]}}", HttpStatusCode.NotFound);
        await AssertCommitStatusAsync(client, apiServer.BaseUrl, $"{{\"previewId\":\"{previewId}\",\"rowIndexes\":[0,0]}}", HttpStatusCode.BadRequest);
        await AssertCommitStatusAsync(client, apiServer.BaseUrl, $"{{\"previewId\":\"{previewId}\",\"rowIndexes\":[1]}}", HttpStatusCode.Conflict);
        await AssertCommitStatusAsync(client, apiServer.BaseUrl, $"{{\"previewId\":\"{previewId}\",\"rowIndexes\":[2]}}", HttpStatusCode.Conflict);

        await AssertCommitStatusAsync(client, apiServer.BaseUrl, $"{{\"previewId\":\"{previewId}\",\"rowIndexes\":[0]}}", HttpStatusCode.OK);
        Assert.Equal(1, eventCount);
        Assert.Contains((await repository.ListComponentsAsync()), component => component.PartNumber == "100nF" && component.Package == "0603");
    }

    [Fact]
    public async Task ApiServer_AuthenticatesLoopbackAndPrivateLanEndpointsAndReleasesPort()
    {
        var privateAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address.ToString())
            .FirstOrDefault(CatalogApiBindingOptions.IsPrivateIpv4);
        Assert.False(string.IsNullOrWhiteSpace(privateAddress));

        var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("lan-smoke", "LAN-SMOKE"));
        var port = GetAvailablePort();
        var options = new CatalogApiBindingOptions(port, lanEnabled: true, lanIpv4: privateAddress);
        using var client = new HttpClient();

        try
        {
            await using var apiServer = await CatalogApiServer.StartAsync(repository, Token, options);
            using var loopbackUnauthorized = await client.GetAsync($"{apiServer.BaseUrl}/taxonomy/categories");
            Assert.Equal(HttpStatusCode.Unauthorized, loopbackUnauthorized.StatusCode);

            using var lanUnauthorized = await client.GetAsync($"{apiServer.BaseUrls[1]}/taxonomy/categories");
            Assert.Equal(HttpStatusCode.Unauthorized, lanUnauthorized.StatusCode);

            using var wrongTokenRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiServer.BaseUrls[1]}/taxonomy/categories");
            wrongTokenRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");
            using var wrongToken = await client.SendAsync(wrongTokenRequest);
            Assert.Equal(HttpStatusCode.Unauthorized, wrongToken.StatusCode);

            using var taxonomy = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrls[1]}/taxonomy/categories"));
            Assert.Equal(HttpStatusCode.OK, taxonomy.StatusCode);

            using var components = await client.SendAsync(CreateAuthorizedRequest(HttpMethod.Get, $"{apiServer.BaseUrls[1]}/components"));
            components.EnsureSuccessStatusCode();
            using var componentsJson = JsonDocument.Parse(await components.Content.ReadAsStringAsync());
            var component = componentsJson.RootElement.GetProperty("results")[0].GetProperty("component");
            Assert.True(component.TryGetProperty("majorCategoryId", out _));
            Assert.True(component.TryGetProperty("subCategoryId", out _));

            using var optionsRequest = new HttpRequestMessage(HttpMethod.Options, $"{apiServer.BaseUrls[1]}/components");
            using var optionsResponse = await client.SendAsync(optionsRequest);
            Assert.Equal(HttpStatusCode.NoContent, optionsResponse.StatusCode);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is HttpListenerException)
        {
            Assert.Contains("URL ACL", exception.Message);
            Console.WriteLine($"LAN listener blocked by URL ACL for {privateAddress}:{port}.");
            return;
        }

        await using var replacement = await CatalogApiServer.StartAsync(repository, Token, new CatalogApiBindingOptions(port));
        Assert.Equal($"http://127.0.0.1:{port}", replacement.BaseUrl);
    }

    [Fact]
    public async Task ApiServer_ExposesOnlyUnauthenticatedReadOnlyMobileSnapshotOnLan()
    {
        var privateAddress = GetPrivateAddress();
        var repository = await CreateRepositoryAsync();
        await repository.SaveComponentAsync(CreateComponent("mobile-stm32", "STM32F103C8T6"));
        var options = new CatalogApiBindingOptions(GetAvailablePort(), lanEnabled: true, lanIpv4: privateAddress);
        using var client = new HttpClient();

        try
        {
            await using var apiServer = await CatalogApiServer.StartAsync(repository, Token, options);

            using var snapshot = await client.GetAsync(options.MobileSnapshotUrl);
            Assert.Equal(HttpStatusCode.OK, snapshot.StatusCode);
            Assert.Contains("no-store", snapshot.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            using var snapshotJson = JsonDocument.Parse(await snapshot.Content.ReadAsStringAsync());
            Assert.True(snapshotJson.RootElement.GetProperty("meta").TryGetProperty("taxonomyVersion", out _));
            Assert.True(snapshotJson.RootElement.GetProperty("meta").TryGetProperty("majorCategories", out _));
            Assert.Equal("mobile-stm32", Assert.Single(snapshotJson.RootElement.GetProperty("components").EnumerateArray()).GetProperty("id").GetString());

            using var managementRead = await client.GetAsync($"{options.BaseUrl}/components");
            Assert.Equal(HttpStatusCode.Unauthorized, managementRead.StatusCode);

            using var loopbackSnapshot = await client.GetAsync($"{options.BaseUrl}/mobile/snapshot");
            Assert.Equal(HttpStatusCode.Unauthorized, loopbackSnapshot.StatusCode);

            using var managementRouteOnLan = await client.GetAsync($"http://{privateAddress}:{options.Port}/components");
            Assert.Equal(HttpStatusCode.Unauthorized, managementRouteOnLan.StatusCode);

            using var mobilePost = await client.PostAsync(options.MobileSnapshotUrl, new StringContent("{", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, mobilePost.StatusCode);

            using var managementWriteOnLan = await client.PostAsync($"http://{privateAddress}:{options.Port}/components", new StringContent("{", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, managementWriteOnLan.StatusCode);

            using var authorizedManagementWriteOnLan = CreateAuthorizedRequest(
                HttpMethod.Post,
                $"http://{privateAddress}:{options.Port}/components",
                new StringContent("{", Encoding.UTF8, "application/json"));
            using var authorizedManagementWriteOnLanResponse = await client.SendAsync(authorizedManagementWriteOnLan);
            Assert.Equal(HttpStatusCode.NotFound, authorizedManagementWriteOnLanResponse.StatusCode);

            using var managementWrite = await client.PostAsync($"{options.BaseUrl}/components", new StringContent("{", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.Unauthorized, managementWrite.StatusCode);

            using var stillAvailable = await client.GetAsync(options.MobileSnapshotUrl);
            Assert.Equal(HttpStatusCode.OK, stillAvailable.StatusCode);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is HttpListenerException)
        {
            Assert.Contains("URL ACL", exception.Message);
            Console.WriteLine($"LAN listener blocked by URL ACL for {privateAddress}:{options.Port}.");
        }
    }

    [Fact]
    public async Task ApiServer_ExposesOnlyFixedReadOnlyAndroidAppReleaseFilesOnLan()
    {
        var privateAddress = GetPrivateAddress();
        var repository = await CreateRepositoryAsync();
        var apkBytes = Encoding.UTF8.GetBytes("android-app-release-v2");
        var apkPath = Path.Combine(_workingDirectory, "app-release.apk");
        await File.WriteAllBytesAsync(apkPath, apkBytes);
        var release = new AndroidAppRelease(apkPath, "com.partshelf.catalog", 2, "1.1", "筛选界面优化与自动更新", 1);
        var options = new CatalogApiBindingOptions(GetAvailablePort(), lanEnabled: true, lanIpv4: privateAddress);
        using var client = new HttpClient();

        try
        {
            await using var apiServer = await CatalogApiServer.StartAsync(repository, Token, options, androidAppRelease: release);

            using var manifestResponse = await client.GetAsync(options.MobileAppUpdateUrl);
            manifestResponse.EnsureSuccessStatusCode();
            Assert.Contains("no-store", manifestResponse.Headers.CacheControl?.ToString(), StringComparison.OrdinalIgnoreCase);
            using var manifest = JsonDocument.Parse(await manifestResponse.Content.ReadAsStringAsync());
            Assert.Equal("com.partshelf.catalog", manifest.RootElement.GetProperty("packageName").GetString());
            Assert.Equal(2, manifest.RootElement.GetProperty("versionCode").GetInt32());
            Assert.Equal("1.1", manifest.RootElement.GetProperty("versionName").GetString());
            Assert.Equal("/mobile/app.apk", manifest.RootElement.GetProperty("apkPath").GetString());
            Assert.Equal(apkBytes.Length, manifest.RootElement.GetProperty("sizeBytes").GetInt64());
            Assert.Equal(Convert.ToHexString(SHA256.HashData(apkBytes)).ToLowerInvariant(), manifest.RootElement.GetProperty("sha256").GetString());

            using var apkResponse = await client.GetAsync(options.MobileAppApkUrl);
            apkResponse.EnsureSuccessStatusCode();
            Assert.Equal("application/vnd.android.package-archive", apkResponse.Content.Headers.ContentType?.MediaType);
            Assert.Equal(apkBytes.Length, apkResponse.Content.Headers.ContentLength);
            Assert.Equal(apkBytes, await apkResponse.Content.ReadAsByteArrayAsync());

            using var apkHead = new HttpRequestMessage(HttpMethod.Head, options.MobileAppApkUrl);
            using var apkHeadResponse = await client.SendAsync(apkHead);
            Assert.Equal(HttpStatusCode.OK, apkHeadResponse.StatusCode);
            Assert.Equal(apkBytes.Length, apkHeadResponse.Content.Headers.ContentLength);

            using var manifestPost = await client.PostAsync(options.MobileAppUpdateUrl, new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, manifestPost.StatusCode);
            using var apkPost = await client.PostAsync(options.MobileAppApkUrl, new StringContent("{}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.MethodNotAllowed, apkPost.StatusCode);
            using var queryAttempt = await client.GetAsync($"{options.MobileAppApkUrl}?path=other.apk");
            Assert.Equal(HttpStatusCode.NotFound, queryAttempt.StatusCode);
        }
        catch (InvalidOperationException exception) when (exception.InnerException is HttpListenerException)
        {
            Assert.Contains("URL ACL", exception.Message);
        }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var disposable in _asyncDisposables)
        {
            await disposable.DisposeAsync();
        }

        if (Directory.Exists(_workingDirectory))
        {
            Directory.Delete(_workingDirectory, recursive: true);
        }
    }

    private async Task<CatalogRepository> CreateRepositoryAsync()
    {
        Directory.CreateDirectory(_workingDirectory);
        var repository = new CatalogRepository(new CatalogRepositoryOptions(
            Path.Combine(_workingDirectory, "catalog.db"),
            Path.Combine(_workingDirectory, "catalog.json")));
        _asyncDisposables.Add(repository);
        await repository.InitializeAsync();
        return repository;
    }

    private static string GetPrivateAddress()
    {
        var privateAddress = NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface => networkInterface.OperationalStatus == OperationalStatus.Up)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address.ToString())
            .FirstOrDefault(CatalogApiBindingOptions.IsPrivateIpv4);
        Assert.False(string.IsNullOrWhiteSpace(privateAddress));
        return privateAddress;
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
        return request;
    }

    private static async Task AssertCommitStatusAsync(HttpClient client, string baseUrl, string payload, HttpStatusCode expectedStatus)
    {
        using var request = CreateAuthorizedRequest(
            HttpMethod.Post,
            $"{baseUrl}/imports/bom/commit",
            new StringContent(payload, Encoding.UTF8, "application/json"));
        using var response = await client.SendAsync(request);
        Assert.Equal(expectedStatus, response.StatusCode);
    }

    private static ComponentRecord CreateComponent(string id, string partNumber, string package = "LQFP48")
    {
        return new ComponentRecord
        {
            Id = id,
            Name = partNumber,
            PartNumber = partNumber,
            Category = "MCU",
            Package = package,
            Aliases = [],
            Keywords = ["test"],
            Attributes = new Dictionary<string, string> { ["key"] = "value" },
            Owned = true,
            Location = "A1",
            Substitutes = [],
            Notes = "test",
            SourceType = ComponentSourceType.Manual,
        };
    }
}
