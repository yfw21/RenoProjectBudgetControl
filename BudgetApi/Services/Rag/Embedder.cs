using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetApi.Services.Rag;

public class Embedder
{
    private readonly HttpClient _http;
    private readonly string _model;

    public Embedder(HttpClient http, IConfiguration cfg)
    {
        var baseUrl = cfg["Ollama:BaseUrl"] ?? "http://localhost:11434";
        _model = cfg["Ollama:EmbedModel"] ?? "nomic-embed-text";
        _http = http;
        _http.BaseAddress = new Uri(baseUrl);
        _http.Timeout = TimeSpan.FromMinutes(2);
    }

    private class EmbedRequest
    {
        [JsonPropertyName("model")] public string Model { get; set; } = "";
        [JsonPropertyName("prompt")] public string Prompt { get; set; } = "";
    }

    private class EmbedResponse
    {
        [JsonPropertyName("embedding")] public float[]? Embedding { get; set; }
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken ct = default)
    {
        var resp = await _http.PostAsJsonAsync("/api/embeddings", new EmbedRequest { Model = _model, Prompt = text }, ct);
        resp.EnsureSuccessStatusCode();
        var data = await resp.Content.ReadFromJsonAsync<EmbedResponse>(cancellationToken: ct);
        return data?.Embedding ?? Array.Empty<float>();
    }

    public static string Serialize(float[] v) => JsonSerializer.Serialize(v);
    public static float[] Deserialize(string s) => JsonSerializer.Deserialize<float[]>(s) ?? Array.Empty<float>();

    public static float Cosine(float[] a, float[] b)
    {
        if (a.Length == 0 || b.Length == 0 || a.Length != b.Length) return 0;
        float dot = 0, na = 0, nb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; na += a[i] * a[i]; nb += b[i] * b[i]; }
        return (float)(dot / (Math.Sqrt(na) * Math.Sqrt(nb) + 1e-9));
    }
}
