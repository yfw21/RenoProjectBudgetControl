namespace BudgetApi.Services.Llm;

public interface ILlmClient
{
    string ModelName { get; }
    Task<OllamaChatResponse> ChatAsync(
        List<OllamaMessage> messages,
        List<OllamaTool>? tools = null,
        CancellationToken ct = default);
}
