using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzDoReviewAgent.AzureDevOps.Models;
using AzDoReviewAgent.Configuration;
using Microsoft.Extensions.Options;

namespace AzDoReviewAgent.AzureDevOps;

public sealed class AzureDevOpsClient : IAzureDevOpsClient
{
    private readonly HttpClient _http;
    private readonly string _apiVersion;
    private readonly ILogger<AzureDevOpsClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public AzureDevOpsClient(
        HttpClient httpClient,
        IOptions<AzureDevOpsOptions> options,
        ILogger<AzureDevOpsClient> logger)
    {
        _http = httpClient;
        _apiVersion = options.Value.ApiVersion;
        _logger = logger;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private string PrBaseUrl(string project, string repositoryId, int pullRequestId) =>
        $"{project}/_apis/git/repositories/{repositoryId}/pullRequests/{pullRequestId}";

    private async Task<T> GetAsync<T>(string url, CancellationToken ct)
    {
        _logger.LogDebug("GET {Url}", url);
        var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("GET {Url} failed with {StatusCode}: {Body}", url, response.StatusCode, body);
            response.EnsureSuccessStatusCode();
        }

        var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
        return result!;
    }

    private async Task PostAsync(string url, object body, CancellationToken ct)
    {
        _logger.LogDebug("POST {Url}", url);
        var response = await _http.PostAsJsonAsync(url, body, JsonOptions, ct);

        if (!response.IsSuccessStatusCode)
        {
            var responseBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("POST {Url} failed with {StatusCode}: {Body}", url, response.StatusCode, responseBody);
            response.EnsureSuccessStatusCode();
        }
    }

    // ── Wrapper for paged/value-envelope responses ───────────────────────────

    private async Task<List<T>> GetListAsync<T>(string url, CancellationToken ct)
    {
        var envelope = await GetAsync<ValueEnvelope<T>>(url, ct);
        return envelope.Value ?? [];
    }

    // ── IAzureDevOpsClient ───────────────────────────────────────────────────

    public Task<PullRequest> GetPullRequestAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        var url = $"{PrBaseUrl(project, repositoryId, pullRequestId)}?api-version={_apiVersion}";
        return GetAsync<PullRequest>(url, ct);
    }

    public Task<List<PullRequestIteration>> GetIterationsAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        var url = $"{PrBaseUrl(project, repositoryId, pullRequestId)}/iterations?api-version={_apiVersion}";
        return GetListAsync<PullRequestIteration>(url, ct);
    }

    public Task<List<PullRequestChange>> GetIterationChangesAsync(
        string project, string repositoryId, int pullRequestId, int iterationId, CancellationToken ct = default)
    {
        var url = $"{PrBaseUrl(project, repositoryId, pullRequestId)}/iterations/{iterationId}/changes?api-version={_apiVersion}";
        return GetListAsync<PullRequestChange>(url, ct);
    }

    public async Task<string?> GetFileContentAsync(
        string project, string repositoryId, string path, string commitId, CancellationToken ct = default)
    {
        var encodedPath = Uri.EscapeDataString(path);
        var url = $"{project}/_apis/git/repositories/{repositoryId}/items" +
                  $"?path={encodedPath}" +
                  $"&versionDescriptor.version={commitId}" +
                  $"&versionDescriptor.versionType=commit" +
                  $"&includeContent=true" +
                  $"&api-version={_apiVersion}";

        var item = await GetAsync<GitItem>(url, ct);

        if (item.ContentMetadata?.IsBinary == true)
        {
            _logger.LogDebug("Skipping binary file {Path}", path);
            return null;
        }

        return item.Content;
    }

    public Task<List<CommentThread>> GetThreadsAsync(
        string project, string repositoryId, int pullRequestId, CancellationToken ct = default)
    {
        var url = $"{PrBaseUrl(project, repositoryId, pullRequestId)}/threads?api-version={_apiVersion}";
        return GetListAsync<CommentThread>(url, ct);
    }

    public Task CreateThreadAsync(
        string project, string repositoryId, int pullRequestId, NewThread thread, CancellationToken ct = default)
    {
        var url = $"{PrBaseUrl(project, repositoryId, pullRequestId)}/threads?api-version={_apiVersion}";

        object? threadContext = null;
        object? pullRequestThreadContext = null;

        if (thread.FilePath is not null)
        {
            threadContext = new
            {
                filePath = thread.FilePath,
                rightFileStart = new { line = thread.StartLine ?? 1, offset = 1 },
                rightFileEnd = new { line = thread.EndLine ?? thread.StartLine ?? 1, offset = 1 }
            };

            if (thread.ChangeTrackingId.HasValue && thread.IterationId.HasValue)
            {
                pullRequestThreadContext = new
                {
                    changeTrackingId = thread.ChangeTrackingId.Value,
                    iterationContext = new
                    {
                        firstComparingIteration = 1,
                        secondComparingIteration = thread.IterationId.Value
                    }
                };
            }
        }

        var body = new
        {
            status = thread.Status,
            comments = new[]
            {
                new { content = thread.Content, commentType = 1 }
            },
            threadContext,
            pullRequestThreadContext
        };

        return PostAsync(url, body, ct);
    }

    public Task ReplyToThreadAsync(
        string project, string repositoryId, int pullRequestId, int threadId, string content, CancellationToken ct = default)
    {
        var url = $"{PrBaseUrl(project, repositoryId, pullRequestId)}/threads/{threadId}/comments?api-version={_apiVersion}";

        var body = new { content, commentType = 1 };

        return PostAsync(url, body, ct);
    }

    public async Task<bool> ValidateConnectionAsync()
    {
        try
        {
            var url = $"_apis/projects?api-version={_apiVersion}&$top=1";
            _logger.LogDebug("Validating Azure DevOps connection via GET {Url}", url);
            var response = await _http.GetAsync(url);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Azure DevOps connection validation failed");
            return false;
        }
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private sealed class ValueEnvelope<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }

        [JsonPropertyName("count")]
        public int Count { get; set; }
    }
}
