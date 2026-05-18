using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AssignResourceTool : ITool
{
    public string Name => "assign_resource";
    public string Description => "Assign a crew or piece of equipment to a task over a date range.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            task_id = new { type = "integer", description = "Task id" },
            resource_kind = new { type = "string", description = "crew or equipment" },
            resource_name = new { type = "string", description = "Name of the crew or equipment" },
            start = new { type = "string", description = "ISO start date" },
            end = new { type = "string", description = "ISO end date" }
        },
        required = new[] { "task_id", "resource_kind", "resource_name", "start", "end" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        if (!args.TryGetProperty("task_id", out var ti) || !ti.TryGetInt32(out var taskId))
            return new { error = "task_id required" };
        var task = await db.CategoryActivities.FindAsync(taskId);
        if (task is null) return new { error = "task not found" };

        if (!args.TryGetProperty("resource_kind", out var rk)) return new { error = "resource_kind required" };
        var kindStr = rk.GetString()?.ToLowerInvariant();
        ResourceKind kind = kindStr == "equipment" ? ResourceKind.Equipment : ResourceKind.Crew;

        var resName = args.TryGetProperty("resource_name", out var rn) ? rn.GetString() : null;
        if (string.IsNullOrWhiteSpace(resName)) return new { error = "resource_name required" };

        int resId;
        if (kind == ResourceKind.Crew)
        {
            var c = await db.Crews.FirstOrDefaultAsync(x => x.Name == resName);
            if (c is null) return new { error = $"crew '{resName}' not found" };
            resId = c.Id;
        }
        else
        {
            var e = await db.Equipment.FirstOrDefaultAsync(x => x.Name == resName);
            if (e is null) return new { error = $"equipment '{resName}' not found" };
            resId = e.Id;
        }

        if (!args.TryGetProperty("start", out var s) || !DateTime.TryParse(s.GetString(), out var start))
            return new { error = "valid start required" };
        if (!args.TryGetProperty("end", out var en) || !DateTime.TryParse(en.GetString(), out var end))
            return new { error = "valid end required" };

        var assignment = new Assignment { TaskId = taskId, ResourceKind = kind, ResourceId = resId, Start = start, End = end };
        db.Assignments.Add(assignment);
        await db.SaveChangesAsync();

        var overlapping = await db.Assignments
            .Where(a => a.ResourceKind == kind && a.ResourceId == resId && a.Id != assignment.Id
                        && a.Start <= end && a.End >= start)
            .CountAsync();
        return new { id = assignment.Id, task = task.Name, resource = resName, start = start.ToString("yyyy-MM-dd"), end = end.ToString("yyyy-MM-dd"), overlap_warning = overlapping > 0 };
    }
}
