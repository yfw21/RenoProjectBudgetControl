using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/budget")]
public class BudgetController : ControllerBase
{
    private readonly BudgetContext _db;
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<BudgetController> _log;
    public BudgetController(BudgetContext db, IConfiguration cfg, IHttpClientFactory httpFactory, ILogger<BudgetController> log)
    {
        _db = db; _cfg = cfg; _httpFactory = httpFactory; _log = log;
    }

    public record BudgetLineDto(int Id, string Description, decimal Budget, decimal Spent, int PercentComplete, decimal Earned, string? Status);
    public record BudgetCategoryDto(string Category, decimal TotalBudget, decimal TotalSpent, decimal TotalEarned, int LineCount, List<BudgetLineDto> Lines);
    public record BudgetViewDto(string Project, decimal TotalBudget, decimal TotalSpent, decimal TotalEarned, int CategoryCount, int LineCount, List<BudgetCategoryDto> Categories);

    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int projectId)
    {
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return NotFound();

        // Planned lines = BudgetItems with no BudgetLineId (they ARE budget lines)
        var planned = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId == null)
            .OrderBy(b => b.Category).ThenBy(b => b.Id)
            .ToListAsync();

        // Expenses linked to lines
        var expenses = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId != null)
            .ToListAsync();
        var spentByLine = expenses
            .GroupBy(e => e.BudgetLineId!.Value)
            .ToDictionary(g => g.Key, g => g.Sum(e => e.Actual));

        var categories = planned
            .GroupBy(b => b.Category ?? "Uncategorized")
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var lines = g.Select(x =>
                {
                    var spent = spentByLine.GetValueOrDefault(x.Id, 0m);
                    var pct = Math.Clamp(x.PercentComplete, 0, 100);
                    var earned = (pct / 100m) * x.Budget;
                    return new BudgetLineDto(x.Id, x.Description, x.Budget, spent, pct, earned, x.Status);
                }).ToList();
                return new BudgetCategoryDto(
                    g.Key,
                    lines.Sum(l => l.Budget),
                    lines.Sum(l => l.Spent),
                    lines.Sum(l => l.Earned),
                    lines.Count,
                    lines);
            }).ToList();

        return Ok(new BudgetViewDto(
            project.Name,
            categories.Sum(c => c.TotalBudget),
            categories.Sum(c => c.TotalSpent),
            categories.Sum(c => c.TotalEarned),
            categories.Count,
            categories.Sum(c => c.LineCount),
            categories
        ));
    }

    public record FlatLineDto(int Id, string Category, string Description, decimal Budget);

    [HttpGet("lines")]
    public async Task<IActionResult> ListLines([FromQuery] int projectId)
    {
        var lines = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId == null && b.Budget > 0)
            .OrderBy(b => b.Category).ThenBy(b => b.Description)
            .Select(b => new FlatLineDto(b.Id, b.Category, b.Description, b.Budget))
            .ToListAsync();
        return Ok(lines);
    }

    public record UpdateLineRequest(string? Description, decimal? Budget, decimal? Actual, string? Category, string? Status, int? PercentComplete);

    [HttpPut("line/{id}")]
    public async Task<IActionResult> UpdateLine(int id, UpdateLineRequest req)
    {
        var item = await _db.BudgetItems.FindAsync(id);
        if (item == null) return NotFound();
        if (req.Description != null) item.Description = req.Description;
        if (req.Budget.HasValue) item.Budget = req.Budget.Value;
        if (req.Actual.HasValue) item.Actual = req.Actual.Value;
        if (req.Category != null) item.Category = req.Category;
        if (req.Status != null) item.Status = req.Status;
        if (req.PercentComplete.HasValue) item.PercentComplete = Math.Clamp(req.PercentComplete.Value, 0, 100);
        await _db.SaveChangesAsync();
        return NoContent();
    }

    public record ValidationFlag(int LineId, string Description, string Issue, string Suggestion);

    [HttpPost("validate")]
    public async Task<IActionResult> Validate([FromQuery] int projectId, CancellationToken ct)
    {
        var items = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId == null)
            .OrderBy(b => b.Category).ThenBy(b => b.Id)
            .ToListAsync(ct);
        if (items.Count == 0) return Ok(new { flags = Array.Empty<ValidationFlag>() });

        var byId = items.ToDictionary(i => i.Id);
        var listing = string.Join("\n", items.Select(i =>
            $"ID {i.Id}: [{i.Category}] {i.Description} = ${i.Budget:F2}"));

        const string systemPrompt = """
            You are reviewing a construction project's budget for issues. Be conservative — only flag GENUINE concerns.

            Flag a line if:
            - Category obviously doesn't match the description (e.g., "cement bags" under "Labor")
            - Amount looks unreasonable for that kind of work
            - Description contains stray numbers, percentages, or partial formulas
            - Two lines look like duplicates

            Do NOT flag minor typos, casing, or generic descriptions.

            Reply with VALID JSON ONLY:
            {"flags": [{"line_id": <int>, "issue": "<short reason>", "suggestion": "<actionable fix>"}]}

            Max 6 flags. If nothing concrete to flag, return {"flags": []}.
            """;

        try
        {
            var content = await CallOllama(systemPrompt, $"Budget lines:\n{listing}", 800, ct);
            using var contentDoc = JsonDocument.Parse(content);
            if (!contentDoc.RootElement.TryGetProperty("flags", out var flagsEl))
                return Ok(new { flags = Array.Empty<ValidationFlag>() });

            var flags = new List<ValidationFlag>();
            foreach (var f in flagsEl.EnumerateArray())
            {
                if (!f.TryGetProperty("line_id", out var idEl) || !idEl.TryGetInt32(out var lineId)) continue;
                if (!byId.TryGetValue(lineId, out var item)) continue;
                var issue = f.TryGetProperty("issue", out var iEl) ? iEl.GetString() ?? "" : "";
                var suggestion = f.TryGetProperty("suggestion", out var sEl) ? sEl.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(issue)) continue;
                flags.Add(new ValidationFlag(lineId, item.Description, issue, suggestion));
            }
            return Ok(new { flags });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { error = e.Message });
        }
    }

    public record EditTextRequest(string Text);
    public record EditProposal(string? Description, string? Category, decimal? Budget, decimal? Actual, string? Status, int? PercentComplete, string Summary);

    [HttpPost("line/{id}/edit-text")]
    public async Task<IActionResult> EditWithText(int id, [FromBody] EditTextRequest req, CancellationToken ct)
    {
        var line = await _db.BudgetItems.FindAsync(id);
        if (line == null) return NotFound();
        if (string.IsNullOrWhiteSpace(req?.Text)) return BadRequest(new { error = "text required" });

        const string systemPrompt = """
            You interpret a user's spoken or typed request to edit a budget line.
            Reply with VALID JSON ONLY (set unmentioned fields to null):
            {"description": <string|null>, "category": <string|null>, "budget": <number|null>, "actual": <number|null>, "status": <"planned"|"in_progress"|"paid"|"cancelled"|null>, "percent_complete": <0-100 int|null>}

            Examples:
            "change amount to 5000" → {"budget": 5000, "description": null, "category": null, "actual": null, "status": null, "percent_complete": null}
            "60 percent complete" → {"percent_complete": 60, ...rest null}
            "spent 2500 and mark paid" → {"actual": 2500, "status": "paid", ...rest null}
            "move to Materials" → {"category": "Materials", ...rest null}
            "rename to Permits and Inspection" → {"description": "Permits and Inspection", ...rest null}
            """;

        var userMsg = $"Current: description=\"{line.Description}\", category=\"{line.Category}\", budget={line.Budget}, actual={line.Actual}, percent_complete={line.PercentComplete}, status=\"{line.Status ?? ""}\"\n\nUser said: \"{req.Text.Trim()}\"";

        try
        {
            var content = await CallOllama(systemPrompt, userMsg, 300, ct);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            string? GetStr(string n) => root.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(el.GetString()) ? el.GetString() : null;
            decimal? GetDec(string n) => root.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetDecimal() : null;
            int? GetInt(string n) => root.TryGetProperty(n, out var el) && el.ValueKind == JsonValueKind.Number ? el.GetInt32() : null;

            var description = GetStr("description");
            var category = GetStr("category");
            var budget = GetDec("budget");
            var actual = GetDec("actual");
            var status = GetStr("status");
            var pct = GetInt("percent_complete");

            var parts = new List<string>();
            if (description != null) parts.Add($"description → \"{description}\"");
            if (category != null) parts.Add($"category → {category}");
            if (budget.HasValue) parts.Add($"budget → ${budget:F2}");
            if (actual.HasValue) parts.Add($"spent → ${actual:F2}");
            if (status != null) parts.Add($"status → {status}");
            if (pct.HasValue) parts.Add($"% complete → {pct}%");
            var summary = parts.Count > 0 ? string.Join(", ", parts) : "(no changes detected)";

            return Ok(new EditProposal(description, category, budget, actual, status, pct, summary));
        }
        catch (Exception e)
        {
            return StatusCode(500, new { error = e.Message });
        }
    }

    private async Task<string> CallOllama(string systemPrompt, string userMsg, int maxTokens, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(_cfg["Ollama:BaseUrl"] ?? "http://localhost:11434");
        http.Timeout = TimeSpan.FromMinutes(3);
        var model = _cfg["Budget:Model"] ?? _cfg["Receipts:Model"] ?? _cfg["Ollama:ChatModel"] ?? "qwen2.5:3b";
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
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama {(int)resp.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
