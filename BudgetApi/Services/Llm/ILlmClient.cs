namespace BudgetApi.Services.Llm;

public interface ILlmClient
{
    string ModelName { get; }
    Task<OllamaChatResponse> ChatAsync(
        List<OllamaMessage> messages,
        List<OllamaTool>? tools = null,
        CancellationToken ct = default);
}

/// <summary>The model is configured but can't serve the request right now (network, rate limit, sign-in).</summary>
public class LlmUnavailableException(string message, Exception inner) : Exception(message, inner);
