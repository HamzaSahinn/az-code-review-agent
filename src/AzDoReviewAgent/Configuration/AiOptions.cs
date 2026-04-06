namespace AzDoReviewAgent.Configuration;

public sealed class AiOptions
{
    public const string SectionName = "Ai";

    public string Provider { get; set; } = "AzureOpenAI";

    public AzureOpenAiSettings AzureOpenAi { get; set; } = new();
    public OllamaSettings Ollama { get; set; } = new();

    public int MaxTokensPerFile { get; set; } = 8000;
    public int MaxTokensPerReview { get; set; } = 100_000;
    public double Temperature { get; set; } = 0.2;
}

public sealed class AzureOpenAiSettings
{
    public string Endpoint { get; set; } = string.Empty;
    public string DeploymentName { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
}

public sealed class OllamaSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string ModelId { get; set; } = "llama3.1";
}
