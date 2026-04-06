namespace AzDoReviewAgent.Review;

public sealed class ReviewSummary
{
    public string OverallAssessment { get; set; } = string.Empty;
    public int Score { get; set; }
    public int TotalFindings { get; set; }
    public int CriticalCount { get; set; }
    public int WarningCount { get; set; }
    public int SuggestionCount { get; set; }

    /// <summary>Markdown-formatted overall summary with sections for critical issues, warnings, and suggestions.</summary>
    public string Summary { get; set; } = string.Empty;
}
