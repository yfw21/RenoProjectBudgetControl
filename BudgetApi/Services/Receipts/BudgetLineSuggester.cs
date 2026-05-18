using System.Net.Http.Json;
using System.Text.Json;
using BudgetApi.Data;
using Microsoft.EntityFrameworkCore;

namespace BudgetApi.Services.Receipts;

public record SuggestedLine(int LineId, string Confidence);

public class BudgetLineSuggester
{
    private readonly BudgetContext _db;
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly ILogger<BudgetLineSuggester> _log;

    public BudgetLineSuggester(BudgetContext db, IHttpClientFactory httpFactory, IConfiguration cfg, ILogger<BudgetLineSuggester> log)
    {
        _db = db; _httpFactory = httpFactory; _cfg = cfg; _log = log;
    }

    public async Task<SuggestedLine?> SuggestAsync(
        int projectId, string? description, string? vendor, string? category, CancellationToken ct)
    {
        var rawLines = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId == null && b.Budget > 0)
            .OrderBy(b => b.Category).ThenBy(b => b.Description)
            .Select(b => new { b.Id, b.Category, b.Description })
            .ToListAsync(ct);

        if (rawLines.Count == 0) return null;

        var lines = rawLines
            .Select(b => (Id: b.Id, Category: b.Category ?? "", Description: b.Description ?? ""))
            .ToList();
        var candidateIds = lines.Select(l => l.Id).ToHashSet();

        // Try a deterministic match first — exact category, or description containment — and skip the LLM
        // when one obvious winner exists. Saves a 5-40s round trip on the common case.
        var deterministic = DeterministicMatch(lines, description, category);
        if (deterministic != null)
            return new SuggestedLine(deterministic.Value, "high");

        var listing = string.Join("\n", lines.Select(l =>
            $"ID {l.Id}: [{l.Category}] {l.Description}"));

        var userMsg =
            $"Budget lines:\n{listing}\n\n" +
            $"New expense: description=\"{description ?? ""}\" vendor=\"{vendor ?? ""}\" category=\"{category ?? ""}\"\n\n" +
            "Which line is the best match?";

        try
        {
            var content = await CallOllama(SystemPrompt, userMsg, 120, ct);
            _log.LogDebug("Matcher raw output: {Content}", content);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (!root.TryGetProperty("line_id", out var idEl) || idEl.ValueKind != JsonValueKind.Number)
                return null;
            var id = idEl.GetInt32();
            if (!candidateIds.Contains(id))
            {
                _log.LogInformation("Suggester returned line_id={Id} not in candidate set", id);
                return null;
            }
            var conf = root.TryGetProperty("confidence", out var cEl) && cEl.ValueKind == JsonValueKind.String
                ? cEl.GetString() ?? "low" : "low";
            return new SuggestedLine(id, conf);
        }
        catch (Exception e)
        {
            _log.LogWarning("Suggester failed: {Msg}", e.Message);
            return null;
        }
    }

    private static int? DeterministicMatch(
        IReadOnlyList<(int Id, string Category, string Description)> lines, string? description, string? category)
    {
        if (string.IsNullOrWhiteSpace(category) && string.IsNullOrWhiteSpace(description)) return null;
        var cat = category?.Trim().ToLowerInvariant() ?? "";
        var desc = description?.Trim().ToLowerInvariant() ?? "";

        if (cat.Length > 0)
        {
            var byCat = lines.Where(l => l.Category.ToLowerInvariant() == cat).ToList();
            if (byCat.Count == 1) return byCat[0].Id;
        }

        if (desc.Length >= 4)
        {
            var byDesc = lines.Where(l =>
            {
                var d = l.Description.ToLowerInvariant();
                return d.Length > 0 && (d.Contains(desc) || desc.Contains(d));
            }).ToList();
            if (byDesc.Count == 1) return byDesc[0].Id;
        }
        return null;
    }

    private const string SystemPrompt =
        "You match a new project expense to ONE budget line from the list. " +
        "Reply with VALID JSON ONLY: " +
        "{\"line_id\": <int from the list, or null>, \"confidence\": \"high\"|\"medium\"|\"low\"}\n\n" +
        "Pick the line whose scope (category + description) most clearly covers what was bought. " +
        "The expense DESCRIPTION is the strongest signal; its category label is generic. " +
        "When two lines share a category, the DESCRIPTION disambiguates. " +
        "Prefer the most specific line that fits. " +
        "Always pick a line if any could plausibly cover this expense — mark \"low\" when guessing. " +
        "Return null only when nothing in the list could relate to this expense at all.";

    private async Task<string> CallOllama(string systemPrompt, string userMsg, int maxTokens, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(_cfg["Ollama:BaseUrl"] ?? "http://localhost:11434");
        // First inference on a freshly loaded mid-size model can exceed 2 min on CPU.
        http.Timeout = TimeSpan.FromMinutes(5);
        // Reasoning task — a small model (3B) tends to over-decline. Prefer a mid model.
        var model = _cfg["Receipts:MatcherModel"]
            ?? _cfg["Budget:Model"]
            ?? _cfg["Ollama:ReasoningModel"]
            ?? _cfg["Ollama:ChatModel"]
            ?? "llama3.1:8b";
        var body = new
        {
            model,
            stream = false,
            format = "json",
            options = new { temperature = 0.1, num_predict = maxTokens },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMsg }
            }
        };
        var resp = await http.PostAsJsonAsync("/api/chat", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama {(int)resp.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "{}";
    }
}
