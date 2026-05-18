using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class RenderGanttTool : ITool
{
    public string Name => "render_gantt";
    public string Description => "Render a Gantt chart of tasks for a project (or across all projects).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Optional project name or id - omit for all projects" }
        },
        required = Array.Empty<string>()
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        int? projectId = null;
        string? projectName = null;
        if (args.TryGetProperty("project", out _))
        {
            var p = await ProjectResolver.ResolveAsync(db, args);
            if (p != null) { projectId = p.Id; projectName = p.Name; }
        }
        var projects = await db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name);
        var tasks = await db.CategoryActivities
            .Where(t => projectId == null || t.ProjectId == projectId)
            .OrderBy(t => t.StartDate)
            .ToListAsync();
        return new
        {
            _component = "gantt",
            title = projectName != null ? $"Schedule: {projectName}" : "Schedule (all projects)",
            data = tasks.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                project = projects.TryGetValue(t.ProjectId, out var pn) ? pn : "?",
                category = t.CategoryValue,
                start = t.StartDate.ToString("yyyy-MM-dd"),
                end = t.EndDate.ToString("yyyy-MM-dd")
            })
        };
    }
}
