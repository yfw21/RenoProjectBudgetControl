using System.Net.Http.Json;

namespace BudgetApi.Services.Llm;

public class OllamaClient : ILlmClient
{
    private readonly HttpClient _http;
    private readonly string _model;

    public OllamaClient(HttpClient http, IConfiguration cfg)
    {
        var baseUrl = cfg["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = cfg["Ollama:ChatModel"] ?? "llama3.1:8b";
        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromMinutes(5);
    }

    public string ModelName => _model;

    public async Task<OllamaChatResponse> ChatAsync(
        List<OllamaMessage> messages,
        List<OllamaTool>? tools = null,
        CancellationToken ct = default)
    {
        var req = new OllamaChatRequest
        {
            Model = _model,
            Messages = messages,
            Tools = tools,
            Stream = false
        };
        var resp = await _http.PostAsJsonAsync("/api/chat", req, ct);
        resp.EnsureSuccessStatusCode();
        var result = await resp.Content.ReadFromJsonAsync<OllamaChatResponse>(cancellationToken: ct);
        return result!;
    }
}
