using System.Text.Json.Serialization;

namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class PullRequestChange
{
    [JsonPropertyName("changeTrackingId")]
    public int ChangeTrackingId { get; set; }

    [JsonPropertyName("changeType")]
    public string ChangeType { get; set; } = string.Empty;

    [JsonPropertyName("item")]
    public PullRequestChangeItem Item { get; set; } = new();
}

public sealed class PullRequestChangeItem
{
    [JsonPropertyName("objectId")]
    public string ObjectId { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("commitId")]
    public string? CommitId { get; set; }
}
