using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Fabric_backup_lite.Core.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Fabric_backup_lite.Core.Services;

public class FabricApiClient : IFabricApiClient
{
    private readonly HttpClient _httpClient;
    private readonly IAuthenticationService _authService;
    private readonly ILogger<FabricApiClient> _logger;
    private readonly AsyncRetryPolicy<HttpResponseMessage> _retryPolicy;
    private readonly int _lroPollingInterval;
    private readonly TimeSpan _lroTimeout = TimeSpan.FromMinutes(5);

    public FabricApiClient(
        IAuthenticationService authService,
        IConfiguration configuration,
        ILogger<FabricApiClient> logger)
    {
        _authService = authService;
        _logger = logger;

        var baseUrl = configuration["Fabric:BaseUrl"] ?? "https://api.fabric.microsoft.com/v1";
        var timeout = configuration.GetValue<int>("Fabric:Timeout", 120);
        var retryAttempts = configuration.GetValue<int>("Fabric:RetryAttempts", 3);
        _lroPollingInterval = configuration.GetValue<int>("Fabric:LROPollingInterval", 2000);

        _httpClient = new HttpClient
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(timeout)
        };

        _retryPolicy = Policy
            .HandleResult<HttpResponseMessage>(r =>
                r.StatusCode == HttpStatusCode.TooManyRequests ||
                r.StatusCode >= HttpStatusCode.InternalServerError)
            .WaitAndRetryAsync(
                retryAttempts,
                retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Request failed with {StatusCode}. Waiting {Delay}s before retry #{Retry}",
                        outcome.Result?.StatusCode,
                        timespan.TotalSeconds,
                        retryCount);
                });
    }

    public async Task<List<Workspace>> GetWorkspacesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching workspaces");

        var request = new HttpRequestMessage(HttpMethod.Get, "workspaces");
        await AddAuthHeaderAsync(request, cancellationToken);

        var response = await ExecuteWithRetryAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<WorkspacesResponse>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        _logger.LogInformation("Found {Count} workspaces", result?.Value?.Count ?? 0);
        return result?.Value ?? new List<Workspace>();
    }

    public async Task<List<FabricItem>> GetWorkspaceItemsAsync(
        string workspaceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching items for workspace {WorkspaceId}", workspaceId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"workspaces/{workspaceId}/items");
        await AddAuthHeaderAsync(request, cancellationToken);

        var response = await ExecuteWithRetryAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var result = JsonSerializer.Deserialize<ItemsResponse>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var items = result?.Value?.Select(item => new FabricItem
        {
            Id = item.Id,
            DisplayName = item.DisplayName,
            Type = ParseItemType(item.Type),
            Description = item.Description ?? string.Empty,
            WorkspaceId = workspaceId
        }).ToList() ?? new List<FabricItem>();

        _logger.LogInformation("Found {Count} items in workspace", items.Count);
        return items;
    }

    public async Task<List<(byte[] content, string partPath)>> GetItemDefinitionAsync(
        string workspaceId,
        string itemId,
        FabricItemType itemType,
        CancellationToken cancellationToken = default)
    {
        var typeSegment = GetTypeSegment(itemType)
            ?? throw new NotSupportedException(
                $"El tipo '{itemType}' no tiene endpoint de exportación en la API de Fabric.");

        var formatQuery = itemType switch
        {
            FabricItemType.Notebook  => "?format=ipynb",
            FabricItemType.Lakehouse => "?format=LakehouseDefinitionV1",
            _                        => string.Empty
        };
        var url = $"workspaces/{workspaceId}/{typeSegment}/{itemId}/getDefinition{formatQuery}";
        _logger.LogInformation("Getting definition for item {ItemId} ({Type}) via {Url}", itemId, itemType, url);

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        await AddAuthHeaderAsync(request, cancellationToken);

        var response = await ExecuteWithRetryAsync(request, cancellationToken);

        if (response.StatusCode != HttpStatusCode.Accepted && response.StatusCode != HttpStatusCode.OK)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("GetDefinition failed: {StatusCode} - {Content}",
                response.StatusCode, errorContent);
            var snippet = errorContent.Length > 400 ? errorContent[..400] : errorContent;
            throw new HttpRequestException(
                $"GetDefinition failed ({(int)response.StatusCode} {response.StatusCode}): {snippet}");
        }

        if (response.StatusCode == HttpStatusCode.OK)
        {
            var immediateContent = await response.Content.ReadAsStringAsync(cancellationToken);
            var definition = JsonSerializer.Deserialize<DefinitionResponse>(immediateContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (definition?.Definition?.Parts != null && definition.Definition.Parts.Count > 0)
                return ExtractDefinitionContent(definition, itemType);

            throw new InvalidOperationException("No definition parts found in immediate response");
        }

        var locationHeader = response.Headers.Location
            ?? throw new InvalidOperationException("No Location header in 202 Accepted response");

        var pollContent = await PollLroAsync(locationHeader, cancellationToken);
        var status = JsonSerializer.Deserialize<LROStatusResponse>(pollContent,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        _logger.LogInformation("[DIAG] LRO Succeeded for {Type}/{ItemId}. Polling body: {Body}",
            itemType, itemId,
            pollContent.Length > 400 ? pollContent[..400] : pollContent);

        if (status?.Definition?.Parts != null && status.Definition.Parts.Count > 0)
            return ExtractDefinitionContent(status, itemType);

        var resultUrl = locationHeader.ToString().TrimEnd('/') + "/result";
        var resultReq  = new HttpRequestMessage(HttpMethod.Get, new Uri(resultUrl));
        await AddAuthHeaderAsync(resultReq, cancellationToken);

        var resultResp = await ExecuteWithRetryAsync(resultReq, cancellationToken);
        var resultJson = await resultResp.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation("[DIAG] Result URL {Url} → {Status} | Body: {Body}",
            resultUrl,
            (int)resultResp.StatusCode,
            resultJson.Length > 800 ? resultJson[..800] : resultJson);

        if (!resultResp.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Result URL returned {resultResp.StatusCode}: {resultJson}");

        var resultDef = JsonSerializer.Deserialize<DefinitionResponse>(resultJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (resultDef?.Definition?.Parts != null && resultDef.Definition.Parts.Count > 0)
            return ExtractDefinitionContent(resultDef, itemType);

        var resultSnippet = resultJson.Length > 300 ? resultJson[..300] : resultJson;
        throw new InvalidOperationException(
            $"LRO succeeded but no definition parts found. HTTP {(int)resultResp.StatusCode}. Body: {resultSnippet}");
    }

    public async Task<List<FabricCapacity>> GetCapacitiesAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching capacities");

        var request = new HttpRequestMessage(HttpMethod.Get, "capacities");
        await AddAuthHeaderAsync(request, cancellationToken);

        var response = await ExecuteWithRetryAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content  = await response.Content.ReadAsStringAsync(cancellationToken);
        var result   = JsonSerializer.Deserialize<CapacitiesResponse>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return result?.Value?.Select(c => new FabricCapacity
        {
            Id          = c.Id,
            DisplayName = c.DisplayName,
            Sku         = c.Sku,
            Region      = c.Region,
            State       = c.State
        }).ToList() ?? new List<FabricCapacity>();
    }

    public async Task<Workspace> CreateWorkspaceAsync(
        string displayName,
        string capacityId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating workspace '{Name}' on capacity {CapacityId}", displayName, capacityId);

        var body    = JsonSerializer.Serialize(new { displayName, capacityId });
        var request = new HttpRequestMessage(HttpMethod.Post, "workspaces")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        await AddAuthHeaderAsync(request, cancellationToken);

        var response = await ExecuteWithRetryAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var content = await response.Content.ReadAsStringAsync(cancellationToken);
        var ws = JsonSerializer.Deserialize<Workspace>(content,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Could not parse created workspace response");

        _logger.LogInformation("Created workspace '{Name}' with ID {Id}", ws.Name, ws.Id);
        return ws;
    }

    public async Task<string> CreateItemWithDefinitionAsync(
        string workspaceId,
        FabricItemType itemType,
        string displayName,
        List<(byte[] content, string partPath)> parts,
        CancellationToken cancellationToken = default)
    {
        var typeSegment = GetTypeSegment(itemType)
            ?? throw new NotSupportedException($"Item type '{itemType}' is not supported for restore.");

        _logger.LogInformation("Creating {Type} '{Name}' in workspace {WorkspaceId}", itemType, displayName, workspaceId);

        object bodyObj;
        if (itemType == FabricItemType.Lakehouse)
        {
            bodyObj = new { displayName };
        }
        else
        {
            var partsArray = parts.Select(p => new
            {
                path        = p.partPath,
                payload     = Convert.ToBase64String(p.content),
                payloadType = "InlineBase64"
            }).ToArray();

            if (itemType == FabricItemType.Notebook)
                bodyObj = new { displayName, definition = new { format = "ipynb", parts = partsArray } };
            else
                bodyObj = new { displayName, definition = new { parts = partsArray } };
        }

        var body    = JsonSerializer.Serialize(bodyObj);
        var url     = $"workspaces/{workspaceId}/{typeSegment}";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        await AddAuthHeaderAsync(request, cancellationToken);

        var response = await ExecuteWithRetryAsync(request, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var created = await response.Content.ReadAsStringAsync(cancellationToken);
            var item    = JsonSerializer.Deserialize<CreateItemResponse>(created,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _logger.LogInformation("Created {Type} '{Name}' synchronously, new ID: {Id}", itemType, displayName, item?.Id);
            return item?.Id ?? string.Empty;
        }

        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            var locationHeader = response.Headers.Location
                ?? throw new InvalidOperationException("No Location header in 202 Accepted response for CreateItem");

            var pollContent = await PollLroAsync(locationHeader, cancellationToken);
            var lroResult   = JsonSerializer.Deserialize<CreateItemLROResult>(pollContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var newId = lroResult?.CreatedItemId ?? lroResult?.ItemId ?? string.Empty;
            _logger.LogInformation("Created {Type} '{Name}' via LRO, new ID: {Id}", itemType, displayName, newId);
            return newId;
        }

        var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
        throw new HttpRequestException(
            $"CreateItem failed ({(int)response.StatusCode} {response.StatusCode}): {errorContent}");
    }

    private async Task<string> PollLroAsync(Uri locationUri, CancellationToken cancellationToken)
    {
        var started = DateTime.UtcNow;
        while (true)
        {
            if (DateTime.UtcNow - started > _lroTimeout)
                throw new TimeoutException($"LRO polling timeout after {_lroTimeout.TotalMinutes} minutes");

            await Task.Delay(_lroPollingInterval, cancellationToken);

            var pollRequest = new HttpRequestMessage(HttpMethod.Get, locationUri);
            await AddAuthHeaderAsync(pollRequest, cancellationToken);

            var pollResponse = await ExecuteWithRetryAsync(pollRequest, cancellationToken);
            var pollContent  = await pollResponse.Content.ReadAsStringAsync(cancellationToken);

            var status = JsonSerializer.Deserialize<LROStatusResponse>(pollContent,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            _logger.LogDebug("LRO Status: {Status}", status?.Status);

            if (status?.Status == "Succeeded") return pollContent;

            if (status?.Status == "Failed")
            {
                var error = status.Error?.Message ?? "Unknown error";
                throw new Exception($"LRO failed: {error}");
            }
        }
    }

    private static string? GetTypeSegment(FabricItemType type) => type switch
    {
        FabricItemType.Notebook          => "notebooks",
        FabricItemType.DataPipeline      => "dataPipelines",
        FabricItemType.Report            => "reports",
        FabricItemType.SemanticModel     => "semanticModels",
        FabricItemType.Dataflow          => "dataflows",
        FabricItemType.Lakehouse         => "lakehouses",
        FabricItemType.KQLDatabase       => "kqldatabases",
        FabricItemType.Eventhouse        => "eventhouses",
        FabricItemType.Environment       => "environments",
        FabricItemType.SparkJobDefinition => "sparkJobDefinitions",
        _                                => null
    };

    private List<(byte[] content, string partPath)> ExtractDefinitionContent(
        DefinitionResponse definition,
        FabricItemType itemType)
    {
        var result = new List<(byte[] content, string partPath)>();

        foreach (var part in definition.Definition.Parts)
        {
            if (string.IsNullOrEmpty(part.Payload))
            {
                _logger.LogWarning("Skipping part '{Path}' — empty payload", part.Path);
                continue;
            }

            var bytes    = Convert.FromBase64String(part.Payload);
            var partPath = !string.IsNullOrEmpty(part.Path)
                ? part.Path
                : $"definition{GetDefaultExtension(itemType)}";

            _logger.LogInformation("Extracted part '{Path}', {Size} bytes", partPath, bytes.Length);
            result.Add((bytes, partPath));
        }

        if (result.Count == 0)
            throw new InvalidOperationException("All definition parts had empty payloads");

        return result;
    }

    private static string GetDefaultExtension(FabricItemType itemType) => itemType switch
    {
        FabricItemType.Report            => ".json",
        FabricItemType.SemanticModel     => ".bim",
        FabricItemType.Notebook          => ".ipynb",
        FabricItemType.DataPipeline      => ".json",
        FabricItemType.Dataflow          => ".json",
        FabricItemType.Lakehouse         => ".json",
        FabricItemType.KQLDatabase       => ".json",
        FabricItemType.Eventhouse        => ".json",
        FabricItemType.Environment       => ".json",
        FabricItemType.SparkJobDefinition => ".json",
        _                                => ".json"
    };

    private FabricItemType ParseItemType(string type)
    {
        return type?.ToLowerInvariant() switch
        {
            "report"             => FabricItemType.Report,
            "semanticmodel"      => FabricItemType.SemanticModel,
            "notebook"           => FabricItemType.Notebook,
            "datapipeline"       => FabricItemType.DataPipeline,
            "dataflow"           => FabricItemType.Dataflow,
            "lakehouse"          => FabricItemType.Lakehouse,
            "warehouse"          => FabricItemType.Warehouse,
            "kqldatabase"        => FabricItemType.KQLDatabase,
            "eventhouse"         => FabricItemType.Eventhouse,
            "environment"        => FabricItemType.Environment,
            "sparkjobdefinition" => FabricItemType.SparkJobDefinition,
            _                    => FabricItemType.Unknown
        };
    }

    private async Task AddAuthHeaderAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await _authService.GetAccessTokenAsync(cancellationToken);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private async Task<HttpResponseMessage> ExecuteWithRetryAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var clonedRequest = await CloneHttpRequestMessageAsync(request);
            return await _httpClient.SendAsync(clonedRequest, cancellationToken);
        });
    }

    private async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            var content = await request.Content.ReadAsStringAsync();
            clone.Content = new StringContent(content, Encoding.UTF8, "application/json");
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return clone;
    }

    private class WorkspacesResponse
    {
        public List<Workspace> Value { get; set; } = new();
    }

    private class ItemsResponse
    {
        public List<ItemDto> Value { get; set; } = new();
    }

    private class ItemDto
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public string? Description { get; set; }
    }

    private class LROStatusResponse : DefinitionResponse
    {
        public string Status { get; set; } = string.Empty;
        public ErrorInfo? Error { get; set; }
    }

    private class DefinitionResponse
    {
        public DefinitionData Definition { get; set; } = new();
    }

    private class DefinitionData
    {
        public List<DefinitionPart> Parts { get; set; } = new();
    }

    private class DefinitionPart
    {
        public string Path { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public string PayloadType { get; set; } = string.Empty;
    }

    private class ErrorInfo
    {
        public string Message { get; set; } = string.Empty;
        public string ErrorCode { get; set; } = string.Empty;
    }

    private class CapacitiesResponse
    {
        public List<CapacityDto> Value { get; set; } = new();
    }

    private class CapacityDto
    {
        public string Id          { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Sku         { get; set; } = string.Empty;
        public string Region      { get; set; } = string.Empty;
        public string State       { get; set; } = string.Empty;
    }

    private class CreateItemResponse
    {
        public string Id          { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Type        { get; set; } = string.Empty;
    }

    private class CreateItemLROResult
    {
        public string? CreatedItemId { get; set; }
        public string? ItemId        { get; set; }
    }
}
