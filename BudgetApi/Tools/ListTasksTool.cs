using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class ListTasksTool : ITool
{
    public string Name => "list_tasks";
    public string Description => "List scheduled tasks on a project, optionally within a date range.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Project name or id (required)" },
            since = new { type = "string", description = "ISO date - tasks ending on/after this date" },
            until = new { type = "string", description = "ISO date - tasks starting on/before this date" }
        },
        required = new[] { "project" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };

        var q = db.CategoryActivities.Where(a => a.ProjectId == project.Id);
        if (args.TryGetProperty("since", out var s) && DateTime.TryParse(s.GetString(), out var since))
            q = q.Where(a => a.EndDate >= since);
        if (args.TryGetProperty("until", out var u) && DateTime.TryParse(u.GetString(), out var until))
            q = q.Where(a => a.StartDate <= until);

        var items = await q.OrderBy(a => a.StartDate).Select(a => new
        {
            a.Id, a.Name, category = a.CategoryValue,
            start = a.StartDate.ToString("yyyy-MM-dd"),
            end = a.EndDate.ToString("yyyy-MM-dd"),
            a.WorkHours
        }).ToListAsync();

        return new { project = project.Name, count = items.Count, tasks = items };
    }
}
