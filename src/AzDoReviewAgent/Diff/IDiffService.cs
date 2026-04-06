namespace AzDoReviewAgent.Diff;

public interface IDiffService
{
    DiffResult ComputeDiff(string? baseContent, string headContent, string filePath, string changeType);
    string FormatDiffForReview(DiffResult.FileChange fileChange, string headContent);
}
