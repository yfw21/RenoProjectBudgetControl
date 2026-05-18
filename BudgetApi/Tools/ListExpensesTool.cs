using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class ListExpensesTool : ITool
{
    public string Name => "list_expenses";
    public string Description => "List expenses for a project. Optionally filter by category or date range.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Project name or id (required)" },
            category = new { type = "string", description = "Optional category filter" },
            since = new { type = "string", description = "ISO date - only expenses on or after this date" },
            until = new { type = "string", description = "ISO date - only expenses on or before this date" }
        },
        required = new[] { "project" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };

        var q = db.BudgetItems.Where(b => b.ProjectId == project.Id);
        if (args.TryGetProperty("category", out var cat) && cat.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(cat.GetString()))
            q = q.Where(b => b.Category == cat.GetString());
        if (args.TryGetProperty("since", out var s) && s.ValueKind == JsonValueKind.String && DateTime.TryParse(s.GetString(), out var since))
            q = q.Where(b => b.Date >= since);
        if (args.TryGetProperty("until", out var u) && u.ValueKind == JsonValueKind.String && DateTime.TryParse(u.GetString(), out var until))
            q = q.Where(b => b.Date <= until);

        var items = await q.OrderByDescending(b => b.Date).ThenByDescending(b => b.Id)
            .Select(b => new
            {
                b.Id, b.Category, b.Description, amount_usd = b.Actual, b.Vendor,
                date = b.Date == null ? null : b.Date.Value.ToString("yyyy-MM-dd")
            }).ToListAsync();

        var total = items.Sum(i => i.amount_usd);
        return new { project = project.Name, total_usd = total, count = items.Count, expenses = items };
    }
}
