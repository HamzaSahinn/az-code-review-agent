using System.Text.Json.Serialization;

namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class CommentThread
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("comments")]
    public List<ThreadComment> Comments { get; set; } = [];

    [JsonPropertyName("threadContext")]
    public ThreadContext? ThreadContext { get; set; }
}
