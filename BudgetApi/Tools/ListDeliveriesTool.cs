using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class ListDeliveriesTool : ITool
{
    public string Name => "list_deliveries";
    public string Description => "List purchase orders / deliveries. Filter by project, status (ordered/received), or date range.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Optional project name or id" },
            status = new { type = "string", description = "ordered | received" },
            since = new { type = "string" },
            until = new { type = "string" }
        },
        required = Array.Empty<string>()
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        int? projectId = null;
        if (args.TryGetProperty("project", out _))
        {
            var p = await ProjectResolver.ResolveAsync(db, args);
            if (p is null) return new { error = "project not found" };
            projectId = p.Id;
        }
        var q = db.PurchaseOrders.AsQueryable();
        if (projectId.HasValue) q = q.Where(po => po.DestinationProjectId == projectId);
        if (args.TryGetProperty("status", out var st) && st.ValueKind == JsonValueKind.String)
            q = q.Where(po => po.Status == st.GetString());
        if (args.TryGetProperty("since", out var s) && DateTime.TryParse(s.GetString(), out var since))
            q = q.Where(po => po.ExpectedDate >= since);
        if (args.TryGetProperty("until", out var u) && DateTime.TryParse(u.GetString(), out var until))
            q = q.Where(po => po.ExpectedDate <= until);

        var projects = await db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name);
        var pos = await q.OrderByDescending(po => po.ExpectedDate).ToListAsync();
        var items = pos.Select(po => new
        {
            id = po.Id, vendor = po.Vendor,
            project = projects.TryGetValue(po.DestinationProjectId, out var pn) ? pn : $"#{po.DestinationProjectId}",
            status = po.Status,
            expected = po.ExpectedDate.ToString("yyyy-MM-dd"),
            actual = po.ActualDate?.ToString("yyyy-MM-dd")
        }).ToList();
        return new { count = items.Count, deliveries = items };
    }
}
