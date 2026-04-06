namespace AzDoReviewAgent.Configuration;

public sealed class ReviewOptions
{
    public const string SectionName = "Review";

    public List<string> TriggerCommands { get; set; } = ["/review", "/ask"];
    public List<string> FileExtensions { get; set; } = [".cs", ".ts", ".js", ".py", ".java", ".go", ".sql"];
    public List<string> ExcludePatterns { get; set; } = ["*.Designer.cs", "*.generated.cs", "*/Migrations/*"];
    public int MaxFilesPerReview { get; set; } = 50;
    public int MaxFileSizeBytes { get; set; } = 102_400;
}
