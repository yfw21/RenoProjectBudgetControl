using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Materials;

namespace BudgetApi.Tools;

public class SuggestReordersTool : ITool
{
    public string Name => "suggest_reorders";
    public string Description => "Suggest materials to reorder before they run out, factoring in stock, burn rate, and vendor lead time.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Optional - omit to scan all projects" },
            buffer_days = new { type = "integer", description = "Safety buffer days on top of lead time (default 5)" }
        },
        required = Array.Empty<string>()
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var svc = ctx.Services.GetRequiredService<BurnRateService>();
        var buffer = args.TryGetProperty("buffer_days", out var b) && b.ValueKind == JsonValueKind.Number ? b.GetInt32() : 5;
        int? projectFilter = null;
        if (args.TryGetProperty("project", out _))
        {
            var p = await ProjectResolver.ResolveAsync(db, args);
            if (p is null) return new { error = "project not found" };
            projectFilter = p.Id;
        }

        var stocks = await db.StockLevels.Where(s => projectFilter == null || s.ProjectId == projectFilter).ToListAsync();
        var leadTimes = await db.VendorLeadTimes.ToListAsync();
        var materials = await db.Materials.ToDictionaryAsync(m => m.Id);
        var projects = await db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name);

        var suggestions = new List<object>();
        foreach (var s in stocks)
        {
            var burn = await svc.AvgDailyBurnAsync(s.ProjectId, s.MaterialId);
            if (burn <= 0) continue;
            var lead = leadTimes.Where(l => l.MaterialId == s.MaterialId).Select(l => (int?)l.AvgLeadDays).DefaultIfEmpty(7).Max() ?? 7;
            var daysLeft = (double)(s.Quantity / burn);
            if (daysLeft <= lead + buffer)
            {
                suggestions.Add(new
                {
                    project = projects.TryGetValue(s.ProjectId, out var pn) ? pn : $"#{s.ProjectId}",
                    material = materials.TryGetValue(s.MaterialId, out var mat) ? mat.Name : $"#{s.MaterialId}",
                    current_stock = s.Quantity,
                    avg_daily_burn = burn,
                    days_left = Math.Round(daysLeft, 1),
                    lead_days = lead,
                    order_by = DateTime.UtcNow.Date.AddDays(daysLeft - lead).ToString("yyyy-MM-dd")
                });
            }
        }
        return new { count = suggestions.Count, suggestions };
    }
}
