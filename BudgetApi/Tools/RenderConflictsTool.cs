using System.Text.Json;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Scheduling;

namespace BudgetApi.Tools;

public class RenderConflictsTool : ITool
{
    public string Name => "render_conflicts";
    public string Description => "Render an inline panel listing all current resource conflicts (double-bookings).";
    public object ParametersSchema => new { type = "object", properties = new { }, required = Array.Empty<string>() };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var detector = ctx.Services.GetRequiredService<ConflictDetector>();
        var conflicts = await detector.DetectAsync();
        return new
        {
            _component = "conflicts",
            title = "Resource conflicts",
            data = conflicts.Select(c => new
            {
                kind = c.Kind.ToString().ToLower(),
                resource = c.ResourceName,
                task_a = c.TaskAName,
                task_b = c.TaskBName,
                from = c.OverlapStart.ToString("yyyy-MM-dd"),
                to = c.OverlapEnd.ToString("yyyy-MM-dd")
            })
        };
    }
}
