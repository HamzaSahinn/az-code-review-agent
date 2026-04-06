using System.Text.Json.Serialization;

namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class ThreadComment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("parentCommentId")]
    public int ParentCommentId { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("commentType")]
    public string CommentType { get; set; } = string.Empty;

    [JsonPropertyName("author")]
    public IdentityRef Author { get; set; } = new();
}
