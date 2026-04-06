namespace AzDoReviewAgent.Review;

public interface IReviewService
{
    Task<List<ReviewFinding>> ReviewFileAsync(
        string filePath,
        string diff,
        string fileContent,
        string prTitle,
        string prDescription,
        CancellationToken ct);

    Task<ReviewSummary> GenerateSummaryAsync(
        string prTitle,
        string prDescription,
        List<ReviewFinding> allFindings,
        CancellationToken ct);

    Task<string> GenerateReplyAsync(
        string threadContext,
        string fileSnippet,
        List<string> conversationHistory,
        CancellationToken ct);
}
