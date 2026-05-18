using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class RenderProjectSummaryTool : ITool
{
    public string Name => "render_project_summary";
    public string Description => "Render a dashboard panel with high-level KPIs for a project.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string" }
        },
        required = new[] { "project" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };

        var today = DateTime.UtcNow.Date;
        var spent = await db.BudgetItems.Where(b => b.ProjectId == project.Id).SumAsync(b => (decimal?)b.Actual) ?? 0m;
        var budget = await db.BudgetItems.Where(b => b.ProjectId == project.Id).SumAsync(b => (decimal?)b.Budget) ?? 0m;
        var tasks = await db.CategoryActivities.Where(t => t.ProjectId == project.Id).ToListAsync();
        var deliveriesPending = await db.PurchaseOrders.CountAsync(p => p.DestinationProjectId == project.Id && p.ActualDate == null);

        return new
        {
            _component = "summary",
            title = project.Name,
            data = new
            {
                project = project.Name,
                status = project.Status,
                address = project.Address,
                client = project.Client,
                spend_usd = spent,
                budget_usd = budget,
                tasks_total = tasks.Count,
                tasks_in_progress = tasks.Count(t => t.StartDate <= today && t.EndDate >= today),
                tasks_upcoming = tasks.Count(t => t.StartDate > today),
                tasks_overdue = tasks.Count(t => t.EndDate < today),
                deliveries_pending = deliveriesPending
            }
        };
    }
}
