namespace AzDoReviewAgent.Review;

public sealed class ReviewFinding
{
    public string FilePath { get; set; } = string.Empty;
    public int StartLine { get; set; }
    public int EndLine { get; set; }

    /// <summary>one of: critical, warning, suggestion, praise</summary>
    public string Severity { get; set; } = "suggestion";

    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string? Suggestion { get; set; }
}
