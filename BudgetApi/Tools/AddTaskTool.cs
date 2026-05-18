using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AddTaskTool : ITool
{
    public string Name => "add_task";
    public string Description => "Schedule a construction task on a project (e.g. 'Pour foundation Mon-Fri').";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            project = new { type = "string", description = "Project name or id (required)" },
            name = new { type = "string", description = "Task name (required)" },
            category = new { type = "string", description = "Trade category (e.g. Plumbing, Electrical, Concrete)" },
            start = new { type = "string", description = "ISO start date YYYY-MM-DD (required)" },
            end = new { type = "string", description = "ISO end date YYYY-MM-DD (required)" },
            work_hours = new { type = "integer", description = "Estimated total work hours" }
        },
        required = new[] { "project", "name", "start", "end" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };

        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return new { error = "name is required" };

        if (!args.TryGetProperty("start", out var s) || !DateTime.TryParse(s.GetString(), out var start))
            return new { error = "valid start date required" };
        if (!args.TryGetProperty("end", out var e) || !DateTime.TryParse(e.GetString(), out var end))
            return new { error = "valid end date required" };
        if (end < start) return new { error = "end must be on/after start" };

        var category = args.TryGetProperty("category", out var c) && c.ValueKind == JsonValueKind.String
            ? (c.GetString() ?? "General") : "General";
        var hours = args.TryGetProperty("work_hours", out var h) && h.ValueKind == JsonValueKind.Number
            ? h.GetInt32() : (int)Math.Max(1, (end - start).TotalDays + 1) * 8;

        if (!db.Categories.Any(cat => cat.Value == category))
            db.Categories.Add(new Category { Value = category });

        var task = new CategoryActivity
        {
            ProjectId = project.Id,
            Name = name!,
            CategoryValue = category,
            StartDate = start,
            EndDate = end,
            WorkHours = hours
        };
        db.CategoryActivities.Add(task);
        await db.SaveChangesAsync();

        return new
        {
            id = task.Id,
            project = project.Name,
            task.Name,
            category = task.CategoryValue,
            start = task.StartDate.ToString("yyyy-MM-dd"),
            end = task.EndDate.ToString("yyyy-MM-dd"),
            task.WorkHours
        };
    }
}
