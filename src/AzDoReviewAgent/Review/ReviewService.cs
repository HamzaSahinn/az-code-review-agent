using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AzDoReviewAgent.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;

namespace AzDoReviewAgent.Review;

public sealed class ReviewService : IReviewService
{
    private readonly IChatCompletionService _chat;
    private readonly AiOptions _aiOptions;
    private readonly ILogger<ReviewService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public ReviewService(Kernel kernel, IOptions<AiOptions> aiOptions, ILogger<ReviewService> logger)
    {
        _chat = kernel.GetRequiredService<IChatCompletionService>();
        _aiOptions = aiOptions.Value;
        _logger = logger;
    }

    // ── ReviewFileAsync ──────────────────────────────────────────────────────

    public async Task<List<ReviewFinding>> ReviewFileAsync(
        string filePath,
        string diff,
        string fileContent,
        string prTitle,
        string prDescription,
        CancellationToken ct)
    {
        var systemPrompt = LoadPrompt("code-review-system.txt");
        var history = new ChatHistory(systemPrompt);

        // Truncate file content if it would exceed token budget
        var truncatedContent = TruncateToTokenBudget(fileContent, _aiOptions.MaxTokensPerFile);

        var userMessage = BuildFileReviewMessage(filePath, prTitle, prDescription, diff, truncatedContent);
        history.AddUserMessage(userMessage);

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = _aiOptions.Temperature
        };

        _logger.LogDebug("Requesting AI review for file {FilePath}", filePath);

        var response = await _chat.GetChatMessageContentAsync(history, settings, cancellationToken: ct);
        var responseText = response.Content ?? string.Empty;

        return ParseFindings(responseText, filePath);
    }

    // ── GenerateSummaryAsync ─────────────────────────────────────────────────

    public async Task<ReviewSummary> GenerateSummaryAsync(
        string prTitle,
        string prDescription,
        List<ReviewFinding> allFindings,
        CancellationToken ct)
    {
        var systemPrompt = LoadPrompt("review-summary.txt");
        var history = new ChatHistory(systemPrompt);

        var userMessage = BuildSummaryMessage(prTitle, prDescription, allFindings);
        history.AddUserMessage(userMessage);

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = _aiOptions.Temperature
        };

        _logger.LogDebug("Requesting AI summary for PR '{PrTitle}' with {FindingCount} findings",
            prTitle, allFindings.Count);

        var response = await _chat.GetChatMessageContentAsync(history, settings, cancellationToken: ct);
        var responseText = response.Content ?? string.Empty;

        return ParseSummary(responseText, allFindings);
    }

    // ── GenerateReplyAsync ───────────────────────────────────────────────────

    public async Task<string> GenerateReplyAsync(
        string threadContext,
        string fileSnippet,
        List<string> conversationHistory,
        CancellationToken ct)
    {
        var systemPrompt = LoadPrompt("conversation-reply.txt");
        var history = new ChatHistory(systemPrompt);

        // Add conversation history
        for (int i = 0; i < conversationHistory.Count; i++)
        {
            if (i % 2 == 0)
                history.AddAssistantMessage(conversationHistory[i]);
            else
                history.AddUserMessage(conversationHistory[i]);
        }

        // Add current context
        var contextMessage = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(threadContext))
        {
            contextMessage.AppendLine("Thread context:");
            contextMessage.AppendLine(threadContext);
            contextMessage.AppendLine();
        }

        if (!string.IsNullOrWhiteSpace(fileSnippet))
        {
            contextMessage.AppendLine("Relevant code:");
            contextMessage.AppendLine("```");
            contextMessage.AppendLine(TruncateToTokenBudget(fileSnippet, 2000));
            contextMessage.AppendLine("```");
        }

        history.AddUserMessage(contextMessage.ToString());

        var settings = new OpenAIPromptExecutionSettings
        {
            Temperature = _aiOptions.Temperature
        };

        var response = await _chat.GetChatMessageContentAsync(history, settings, cancellationToken: ct);
        return response.Content ?? string.Empty;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static string LoadPrompt(string fileName)
    {
        var resourceName = $"AzDoReviewAgent.Review.Prompts.{fileName}";
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(resourceName);

        if (stream is null)
            throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int EstimateTokens(string text) => text.Length / 4;

    private string TruncateToTokenBudget(string text, int maxTokens)
    {
        if (EstimateTokens(text) <= maxTokens)
            return text;

        var maxChars = maxTokens * 4;
        _logger.LogWarning("Truncating content from {Original} chars to {Max} chars to fit token budget",
            text.Length, maxChars);
        return text[..maxChars] + "\n... [content truncated to fit token budget]";
    }

    private static string BuildFileReviewMessage(
        string filePath,
        string prTitle,
        string prDescription,
        string diff,
        string fileContent)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PR Title: {prTitle}");
        sb.AppendLine($"PR Description: {prDescription}");
        sb.AppendLine();
        sb.AppendLine($"File: {filePath}");
        sb.AppendLine();
        sb.AppendLine("Diff (line numbers reference the new file):");
        sb.AppendLine(diff);
        sb.AppendLine();
        sb.AppendLine("Full file content:");
        sb.AppendLine("```");
        sb.AppendLine(fileContent);
        sb.AppendLine("```");
        return sb.ToString();
    }

    private static string BuildSummaryMessage(
        string prTitle,
        string prDescription,
        List<ReviewFinding> allFindings)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"PR Title: {prTitle}");
        sb.AppendLine($"PR Description: {prDescription}");
        sb.AppendLine();
        sb.AppendLine($"Total findings: {allFindings.Count}");
        sb.AppendLine();

        foreach (var finding in allFindings)
        {
            sb.AppendLine($"[{finding.Severity.ToUpperInvariant()}] {finding.FilePath}:{finding.StartLine}-{finding.EndLine}");
            sb.AppendLine($"  Title: {finding.Title}");
            sb.AppendLine($"  Message: {finding.Message}");
            if (!string.IsNullOrWhiteSpace(finding.Suggestion))
                sb.AppendLine($"  Suggestion: {finding.Suggestion}");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private List<ReviewFinding> ParseFindings(string responseText, string filePath)
    {
        var cleaned = CleanJsonResponse(responseText);

        try
        {
            var raw = JsonSerializer.Deserialize<List<RawFinding>>(cleaned, JsonOptions);
            if (raw is null)
                return [];

            var findings = new List<ReviewFinding>();
            foreach (var r in raw)
            {
                var finding = new ReviewFinding
                {
                    FilePath = string.IsNullOrWhiteSpace(r.FilePath) ? filePath : r.FilePath,
                    StartLine = Math.Max(1, r.StartLine),
                    EndLine = Math.Max(1, r.EndLine),
                    Severity = NormalizeSeverity(r.Severity),
                    Title = (r.Title ?? string.Empty)[..Math.Min(r.Title?.Length ?? 0, 80)],
                    Message = r.Message ?? string.Empty,
                    Suggestion = r.Suggestion
                };

                // Ensure EndLine >= StartLine
                if (finding.EndLine < finding.StartLine)
                    finding.EndLine = finding.StartLine;

                findings.Add(finding);
            }

            return findings;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI findings response. Raw: {Response}",
                cleaned.Length > 500 ? cleaned[..500] : cleaned);
            return [];
        }
    }

    private ReviewSummary ParseSummary(string responseText, List<ReviewFinding> allFindings)
    {
        var cleaned = CleanJsonResponse(responseText);

        try
        {
            var raw = JsonSerializer.Deserialize<RawSummary>(cleaned, JsonOptions);
            if (raw is null)
                return BuildFallbackSummary(allFindings);

            return new ReviewSummary
            {
                OverallAssessment = raw.OverallAssessment ?? string.Empty,
                Score = Math.Clamp(raw.Score, 1, 100),
                TotalFindings = raw.TotalFindings > 0 ? raw.TotalFindings : allFindings.Count,
                CriticalCount = raw.CriticalCount,
                WarningCount = raw.WarningCount,
                SuggestionCount = raw.SuggestionCount,
                Summary = raw.Summary ?? string.Empty
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse AI summary response. Falling back to computed summary.");
            return BuildFallbackSummary(allFindings);
        }
    }

    private static ReviewSummary BuildFallbackSummary(List<ReviewFinding> allFindings)
    {
        int critical = allFindings.Count(f => f.Severity == "critical");
        int warning = allFindings.Count(f => f.Severity == "warning");
        int suggestion = allFindings.Count(f => f.Severity == "suggestion");

        return new ReviewSummary
        {
            OverallAssessment = "Review completed.",
            Score = critical > 0 ? 40 : warning > 0 ? 65 : 85,
            TotalFindings = allFindings.Count,
            CriticalCount = critical,
            WarningCount = warning,
            SuggestionCount = suggestion,
            Summary = $"Found {critical} critical issue(s), {warning} warning(s), and {suggestion} suggestion(s)."
        };
    }

    private static string CleanJsonResponse(string text)
    {
        var t = text.Trim();

        // Strip markdown code fences if present
        if (t.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            t = t["```json".Length..];
        else if (t.StartsWith("```"))
            t = t[3..];

        if (t.EndsWith("```"))
            t = t[..^3];

        return t.Trim();
    }

    private static string NormalizeSeverity(string? severity) =>
        severity?.ToLowerInvariant() switch
        {
            "critical" => "critical",
            "warning" => "warning",
            "suggestion" => "suggestion",
            "praise" => "praise",
            _ => "suggestion"
        };

    // ── Private DTOs for JSON deserialization ────────────────────────────────

    private sealed class RawFinding
    {
        [JsonPropertyName("filePath")]
        public string? FilePath { get; set; }

        [JsonPropertyName("startLine")]
        public int StartLine { get; set; }

        [JsonPropertyName("endLine")]
        public int EndLine { get; set; }

        [JsonPropertyName("severity")]
        public string? Severity { get; set; }

        [JsonPropertyName("title")]
        public string? Title { get; set; }

        [JsonPropertyName("message")]
        public string? Message { get; set; }

        [JsonPropertyName("suggestion")]
        public string? Suggestion { get; set; }
    }

    private sealed class RawSummary
    {
        [JsonPropertyName("overallAssessment")]
        public string? OverallAssessment { get; set; }

        [JsonPropertyName("score")]
        public int Score { get; set; }

        [JsonPropertyName("totalFindings")]
        public int TotalFindings { get; set; }

        [JsonPropertyName("criticalCount")]
        public int CriticalCount { get; set; }

        [JsonPropertyName("warningCount")]
        public int WarningCount { get; set; }

        [JsonPropertyName("suggestionCount")]
        public int SuggestionCount { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }
    }
}
