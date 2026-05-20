using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly BudgetContext _db;
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ReportsController> _log;

    public ReportsController(BudgetContext db, IConfiguration cfg, IHttpClientFactory httpFactory, ILogger<ReportsController> log)
    {
        _db = db; _cfg = cfg; _httpFactory = httpFactory; _log = log;
    }

    private const string UNDATED = "undated";

    /// <summary>Month key "yyyy-MM" for an expense, or "undated" when no date is set.</summary>
    private static string MonthKey(DateTime? d) => d.HasValue ? d.Value.ToString("yyyy-MM") : UNDATED;

    /// <summary>
    /// Per budget line, spend broken into month columns. The control sheet's
    /// "Cync payment 1..6" columns map naturally onto sequential months.
    /// Expenses not linked to a line are grouped under an "Unassigned" row.
    /// </summary>
    [HttpGet("spend-by-month")]
    public async Task<IActionResult> SpendByMonth([FromQuery] int projectId, CancellationToken ct)
    {
        var lines = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId == null && b.Budget > 0)
            .OrderBy(b => b.Category).ThenBy(b => b.Description)
            .Select(b => new { b.Id, b.Category, b.Description, b.Budget })
            .ToListAsync(ct);

        var expenses = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.Actual > 0)
            .Select(b => new { b.BudgetLineId, b.Actual, b.Date })
            .ToListAsync(ct);

        var months = expenses.Select(e => MonthKey(e.Date)).Distinct().OrderBy(SortKey).ToList();

        var spentByLineMonth = expenses
            .Where(e => e.BudgetLineId.HasValue)
            .GroupBy(e => e.BudgetLineId!.Value)
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(e => MonthKey(e.Date)).ToDictionary(mg => mg.Key, mg => mg.Sum(x => x.Actual)));

        var rows = lines.Select(l =>
        {
            var byMonth = spentByLineMonth.GetValueOrDefault(l.Id, new());
            var total = byMonth.Values.Sum();
            return new
            {
                lineId = l.Id,
                category = l.Category,
                description = l.Description,
                budget = l.Budget,
                byMonth,
                total,
                remaining = l.Budget - total
            };
        }).ToList();

        // Expenses with no linked budget line.
        var unassigned = expenses.Where(e => !e.BudgetLineId.HasValue).ToList();
        object? unassignedRow = null;
        if (unassigned.Count > 0)
        {
            var byMonth = unassigned.GroupBy(e => MonthKey(e.Date)).ToDictionary(g => g.Key, g => g.Sum(x => x.Actual));
            unassignedRow = new { byMonth, total = byMonth.Values.Sum() };
        }

        var monthTotals = months.ToDictionary(
            m => m,
            m => expenses.Where(e => MonthKey(e.Date) == m).Sum(e => e.Actual));

        return Ok(new
        {
            months,
            rows,
            unassigned = unassignedRow,
            monthTotals,
            grandTotal = expenses.Sum(e => e.Actual),
            totalBudget = lines.Sum(l => l.Budget)
        });
    }

    /// <summary>
    /// Tax pivot: expense category × month, with row and column totals.
    /// Mirrors the "For tax expense" sheet in the control workbook.
    /// </summary>
    [HttpGet("tax-pivot")]
    public async Task<IActionResult> TaxPivot([FromQuery] int projectId, CancellationToken ct)
    {
        var expenses = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.Actual > 0)
            .Select(b => new { b.Category, b.Actual, b.Date })
            .ToListAsync(ct);

        var months = expenses.Select(e => MonthKey(e.Date)).Distinct().OrderBy(SortKey).ToList();

        var categories = expenses
            .GroupBy(e => string.IsNullOrWhiteSpace(e.Category) ? "Uncategorized" : e.Category)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var byMonth = g.GroupBy(e => MonthKey(e.Date)).ToDictionary(mg => mg.Key, mg => mg.Sum(x => x.Actual));
                return new { category = g.Key, byMonth, total = byMonth.Values.Sum() };
            })
            .ToList();

        var monthTotals = months.ToDictionary(
            m => m,
            m => expenses.Where(e => MonthKey(e.Date) == m).Sum(e => e.Actual));

        return Ok(new
        {
            months,
            categories,
            monthTotals,
            grandTotal = expenses.Sum(e => e.Actual)
        });
    }

    /// <summary>Sort "undated" last; otherwise chronological by the yyyy-MM string.</summary>
    private static string SortKey(string monthKey) => monthKey == UNDATED ? "9999-99" : monthKey;

    public record AskRequest(string Text);

    [HttpPost("ask")]
    public async Task<IActionResult> Ask([FromQuery] int projectId, [FromBody] AskRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Text)) return BadRequest(new { error = "text required" });
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return NotFound();

        var planned = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId == null)
            .ToListAsync(ct);
        var expenses = await _db.BudgetItems
            .Where(b => b.ProjectId == projectId && b.BudgetLineId != null)
            .ToListAsync(ct);
        var spentByLine = expenses.GroupBy(e => e.BudgetLineId!.Value).ToDictionary(g => g.Key, g => g.Sum(e => e.Actual));

        var lines = planned.Select(l =>
        {
            var spent = spentByLine.GetValueOrDefault(l.Id, 0m);
            var earned = (Math.Clamp(l.PercentComplete, 0, 100) / 100m) * l.Budget;
            return new
            {
                id = l.Id,
                category = l.Category,
                description = l.Description,
                budget = l.Budget,
                spent,
                percent_complete = l.PercentComplete,
                earned,
                remaining = l.Budget - spent,
                status = l.Status
            };
        }).ToList();

        var totals = new
        {
            project = project.Name,
            total_planned = lines.Sum(l => l.budget),
            total_spent = lines.Sum(l => l.spent),
            total_earned = lines.Sum(l => l.earned),
            total_remaining = lines.Sum(l => l.remaining),
            line_count = lines.Count
        };

        var contextJson = JsonSerializer.Serialize(new { totals, lines });

        var systemPrompt = """
            You are a project assistant for a construction project. Answer the user's question using the budget data provided.

            Rules:
            - Be concise. 1-3 short paragraphs OR a simple bulleted list.
            - Always cite specific numbers from the data — never make any up.
            - If the user asks "what's over budget" or similar, list lines where spent > budget OR earned < spent.
            - If the user asks "where am I behind" → list lines with low percent_complete vs. high spent.
            - All amounts in USD; format like "$1,234.56".
            - If the question can't be answered from the data, say so clearly.
            """;

        var userMsg = $"Budget data (JSON):\n{contextJson}\n\nUser question: {req.Text.Trim()}";

        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(_cfg["Ollama:BaseUrl"] ?? "http://localhost:11434");
        http.Timeout = TimeSpan.FromMinutes(3);
        var model = _cfg["Reports:Model"] ?? _cfg["Ollama:ChatModel"] ?? "qwen2.5:3b";

        var body = new
        {
            model,
            stream = false,
            options = new { temperature = 0.2, num_predict = 600 },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMsg }
            }
        };

        try
        {
            var resp = await http.PostAsJsonAsync("/api/chat", body, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode) return StatusCode((int)resp.StatusCode, new { error = json });
            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
            return Ok(new { answer = content.Trim() });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { error = e.Message });
        }
    }
}
