using System.Text.Json;
using BudgetApi.Services.Llm;
using BudgetApi.Services.Scheduling;

namespace BudgetApi.Tools;

public class ListConflictsTool : ITool
{
    public string Name => "list_conflicts";
    public string Description => "Find resource double-bookings (crews or equipment assigned to overlapping tasks).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            since = new { type = "string", description = "ISO date - only conflicts ending on or after this date" },
            until = new { type = "string", description = "ISO date - only conflicts starting on or before this date" }
        },
        required = Array.Empty<string>()
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var detector = ctx.Services.GetRequiredService<ConflictDetector>();
        DateTime? since = null, until = null;
        if (args.TryGetProperty("since", out var s) && DateTime.TryParse(s.GetString(), out var sd)) since = sd;
        if (args.TryGetProperty("until", out var u) && DateTime.TryParse(u.GetString(), out var ud)) until = ud;
        var conflicts = await detector.DetectAsync(since, until);
        return new
        {
            count = conflicts.Count,
            conflicts = conflicts.Select(c => new
            {
                kind = c.Kind.ToString().ToLower(),
                resource = c.ResourceName,
                task_a = c.TaskAName,
                task_b = c.TaskBName,
                overlap_start = c.OverlapStart.ToString("yyyy-MM-dd"),
                overlap_end = c.OverlapEnd.ToString("yyyy-MM-dd")
            })
        };
    }
}
