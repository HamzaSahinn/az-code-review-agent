using System.Text.Json.Serialization;

namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class GitItem
{
    [JsonPropertyName("objectId")]
    public string ObjectId { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("content")]
    public string? Content { get; set; }

    [JsonPropertyName("contentMetadata")]
    public GitItemContentMetadata? ContentMetadata { get; set; }
}

public sealed class GitItemContentMetadata
{
    [JsonPropertyName("isBinary")]
    public bool IsBinary { get; set; }
}
