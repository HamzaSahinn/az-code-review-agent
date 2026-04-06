using System.Text.Json.Serialization;

namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class PullRequestIteration
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("author")]
    public IdentityRef Author { get; set; } = new();

    [JsonPropertyName("createdDate")]
    public DateTimeOffset CreatedDate { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("sourceRefCommit")]
    public GitCommitRef? SourceRefCommit { get; set; }

    [JsonPropertyName("targetRefCommit")]
    public GitCommitRef? TargetRefCommit { get; set; }
}
