using System.Text.Json.Serialization;

namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class ThreadContext
{
    [JsonPropertyName("filePath")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("rightFileStart")]
    public CommentPosition? RightFileStart { get; set; }

    [JsonPropertyName("rightFileEnd")]
    public CommentPosition? RightFileEnd { get; set; }
}

public sealed class CommentPosition
{
    [JsonPropertyName("line")]
    public int Line { get; set; }

    [JsonPropertyName("offset")]
    public int Offset { get; set; }
}
