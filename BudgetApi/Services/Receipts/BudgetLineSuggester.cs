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

    public record BatchInput(string Description, string? Vendor);
    public record BatchResult(int ItemIndex, int? LineId, string? Confidence);

    /// <summary>
    /// Matches many inputs against the project's budget lines in a single LLM round trip.
    /// Used by the invoice-split flow to avoid N sequential calls.
    /// </summary>
    public async Task<List<BatchResult>> SuggestBatchAsync(
        int projectId, IReadOnlyList<BatchInput> inputs, CancellationToken ct)
    {
        var defaults = Enumerable.Range(0, inputs.Count)
            .Select(i => new BatchResult(i, null, null))
            .ToList();
        if (inputs.Count == 0) return defaults;

        var rawLines = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId == null && b.Budget > 0)
            .OrderBy(b => b.Category).ThenBy(b => b.Description)
            .Select(b => new { b.Id, b.Category, b.Description })
            .ToListAsync(ct);
        if (rawLines.Count == 0) return defaults;

        var candidateIds = rawLines.Select(l => l.Id).ToHashSet();
        var listing = string.Join("\n", rawLines.Select(l =>
            $"ID {l.Id}: [{l.Category ?? ""}] {l.Description ?? ""}"));
        var inputListing = string.Join("\n", inputs.Select((it, i) =>
            $"ITEM {i}: description=\"{it.Description ?? ""}\" vendor=\"{it.Vendor ?? ""}\""));

        var userMsg =
            $"Budget lines:\n{listing}\n\n" +
            $"Items to match:\n{inputListing}\n\n" +
            "Return one match per ITEM.";

        // Each match is ~25 tokens (item_id, line_id, confidence). Add ~50 token header
        // for the {"matches": [...]} wrapper. Tight cap so the model can't run on.
        var tokenBudget = Math.Min(1000, 50 + inputs.Count * 25);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var content = await CallOllama(BatchSystemPrompt, userMsg, tokenBudget, ct);
            sw.Stop();
            _log.LogInformation("BatchSuggester timing: {Ms}ms for {N} items, {Lines} budget lines, tokenBudget={Tokens}",
                sw.ElapsedMilliseconds, inputs.Count, rawLines.Count, tokenBudget);
            _log.LogDebug("Batch matcher raw output: {Content}", content);
            using var doc = JsonDocument.Parse(content);
            if (!doc.RootElement.TryGetProperty("matches", out var matchesEl)) return defaults;

            foreach (var m in matchesEl.EnumerateArray())
            {
                if (!m.TryGetProperty("item_id", out var idxEl) || !idxEl.TryGetInt32(out var idx)) continue;
                if (idx < 0 || idx >= inputs.Count) continue;
                int? lineId = null;
                if (m.TryGetProperty("line_id", out var lEl) && lEl.ValueKind == JsonValueKind.Number)
                {
                    var l = lEl.GetInt32();
                    if (candidateIds.Contains(l)) lineId = l;
                }
                string? conf = m.TryGetProperty("confidence", out var cEl) && cEl.ValueKind == JsonValueKind.String
                    ? cEl.GetString() : null;
                defaults[idx] = new BatchResult(idx, lineId, conf);
            }
        }
        catch (Exception e)
        {
            _log.LogWarning("Batch suggester failed: {Msg}", e.Message);
        }
        return defaults;
    }

    private const string BatchSystemPrompt =
        "You match each input expense to ONE budget line from the list. " +
        "Reply with VALID JSON ONLY: " +
        "{\"matches\": [{\"item_id\": <int>, \"line_id\": <int|null>, \"confidence\": \"high\"|\"medium\"|\"low\"}]}\n\n" +
        "Output one entry per input ITEM, using the same item_id. " +
        "Pick the budget line whose scope (category + description) most clearly covers what was bought. " +
        "The DESCRIPTION is the strongest signal; the category is generic. " +
        "When two lines share a category, the DESCRIPTION disambiguates. " +
        "Prefer the most specific line that fits. " +
        "Use line_id=null only when nothing in the list could relate to that item at all; " +
        "otherwise pick the best candidate and mark \"low\" when guessing.";

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
