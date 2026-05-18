using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AddTaskDependencyTool : ITool
{
    public string Name => "add_task_dependency";
    public string Description => "Declare that one task depends on another finishing first (predecessor).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            task_id = new { type = "integer" },
            depends_on_task_id = new { type = "integer" }
        },
        required = new[] { "task_id", "depends_on_task_id" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        if (!args.TryGetProperty("task_id", out var t) || !t.TryGetInt32(out var taskId)) return new { error = "task_id required" };
        if (!args.TryGetProperty("depends_on_task_id", out var d) || !d.TryGetInt32(out var depId)) return new { error = "depends_on_task_id required" };
        if (taskId == depId) return new { error = "task cannot depend on itself" };

        if (await db.TaskDependencies.AnyAsync(x => x.TaskId == taskId && x.DependsOnTaskId == depId))
            return new { ok = true, note = "dependency already exists" };

        db.TaskDependencies.Add(new TaskDependency { TaskId = taskId, DependsOnTaskId = depId });
        await db.SaveChangesAsync();
        return new { ok = true };
    }
}
