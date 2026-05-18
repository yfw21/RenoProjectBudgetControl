using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class RenderTableTool : ITool
{
    public string Name => "render_table";
    public string Description => "Render an inline table panel in chat for projects, expenses, tasks, deliveries, conflicts, or crews. Use this when the user asks to 'show' or 'see' a list visually.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            entity = new { type = "string", description = "projects | expenses | tasks | deliveries | conflicts | crews | equipment | materials" },
            project = new { type = "string", description = "Optional project filter" }
        },
        required = new[] { "entity" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var entity = args.TryGetProperty("entity", out var e) ? (e.GetString() ?? "").ToLowerInvariant() : "";
        int? projectId = null;
        if (args.TryGetProperty("project", out _))
        {
            var p = await ProjectResolver.ResolveAsync(db, args);
            if (p != null) projectId = p.Id;
        }

        object data = entity switch
        {
            "projects" => await db.Projects.OrderByDescending(p => p.Id)
                .Select(p => new { p.Id, p.Name, p.Status, p.Address, p.Client }).ToListAsync(),
            "expenses" => await db.BudgetItems.Where(b => projectId == null || b.ProjectId == projectId)
                .OrderByDescending(b => b.Date)
                .Select(b => new { b.Id, b.Category, b.Description, amount_usd = b.Actual, b.Vendor, b.Date }).ToListAsync(),
            "tasks" => await db.CategoryActivities.Where(t => projectId == null || t.ProjectId == projectId)
                .OrderBy(t => t.StartDate)
                .Select(t => new { t.Id, t.Name, category = t.CategoryValue, start = t.StartDate, end = t.EndDate, t.WorkHours }).ToListAsync(),
            "deliveries" => await db.PurchaseOrders.Where(p => projectId == null || p.DestinationProjectId == projectId)
                .OrderByDescending(p => p.ExpectedDate)
                .Select(p => new { p.Id, p.Vendor, p.Status, expected = p.ExpectedDate, actual = p.ActualDate }).ToListAsync(),
            "crews" => await db.Crews.Select(c => new { c.Id, c.Name, c.Headcount, c.Skills, c.DayRateUsd }).ToListAsync(),
            "equipment" => await db.Equipment.Select(eq => new { eq.Id, eq.Name, eq.Type, eq.DayRateUsd, eq.Rented }).ToListAsync(),
            "materials" => await db.Materials.Select(m => new { m.Id, m.Name, m.Unit, m.DefaultUnitPriceUsd }).ToListAsync(),
            _ => (object)new[] { new { error = "unknown entity" } }
        };

        return new
        {
            _component = "table",
            title = $"{entity[..1].ToUpper()}{entity[1..]}{(projectId != null ? " (filtered)" : "")}",
            data
        };
    }
}
