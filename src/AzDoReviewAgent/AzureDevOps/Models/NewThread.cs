namespace AzDoReviewAgent.AzureDevOps.Models;

public sealed class NewThread
{
    public string Content { get; set; } = string.Empty;

    /// <summary>Thread status: 1 = Active (default)</summary>
    public int Status { get; set; } = 1;

    /// <summary>null for PR-level comments</summary>
    public string? FilePath { get; set; }

    /// <summary>Starting line for line-level comments</summary>
    public int? StartLine { get; set; }

    /// <summary>Ending line for line-level comments</summary>
    public int? EndLine { get; set; }

    /// <summary>Required for line-level comments</summary>
    public int? ChangeTrackingId { get; set; }

    /// <summary>Iteration context for line-level comments</summary>
    public int? IterationId { get; set; }
}
