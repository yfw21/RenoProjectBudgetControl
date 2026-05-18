using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class ProjectSummaryTool : ITool
{
    public string Name => "project_summary";
    public string Description => "Get a high-level summary of a project: status, spend total, task counts, upcoming and overdue items.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Project name or id (required)" }
        },
        required = new[] { "project" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };

        var today = DateTime.UtcNow.Date;
        var totalSpent = await db.BudgetItems.Where(b => b.ProjectId == project.Id).SumAsync(b => (decimal?)b.Actual) ?? 0m;
        var totalBudget = await db.BudgetItems.Where(b => b.ProjectId == project.Id).SumAsync(b => (decimal?)b.Budget) ?? 0m;
        var expenseCount = await db.BudgetItems.CountAsync(b => b.ProjectId == project.Id);

        var tasks = await db.CategoryActivities.Where(a => a.ProjectId == project.Id).ToListAsync();
        var inProgress = tasks.Count(a => a.StartDate <= today && a.EndDate >= today);
        var upcoming = tasks.Count(a => a.StartDate > today);
        var overdue = tasks.Count(a => a.EndDate < today);
        var nextStart = tasks.Where(a => a.StartDate > today).OrderBy(a => a.StartDate).FirstOrDefault();

        return new
        {
            project = project.Name,
            status = project.Status,
            address = project.Address,
            client = project.Client,
            spend_usd = totalSpent,
            budget_usd = totalBudget,
            expense_count = expenseCount,
            tasks_total = tasks.Count,
            tasks_in_progress = inProgress,
            tasks_upcoming = upcoming,
            tasks_overdue = overdue,
            next_task = nextStart == null ? null : new
            {
                nextStart.Name,
                start = nextStart.StartDate.ToString("yyyy-MM-dd")
            }
        };
    }
}
