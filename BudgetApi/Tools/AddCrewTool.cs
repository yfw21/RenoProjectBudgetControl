using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AddCrewTool : ITool
{
    public string Name => "add_crew";
    public string Description => "Add a crew (team) to the resource pool.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "Crew name (required)" },
            headcount = new { type = "integer", description = "Number of workers" },
            skills = new { type = "string", description = "Skills / trades (e.g. masonry, plumbing)" },
            day_rate_usd = new { type = "number", description = "Day rate in USD" }
        },
        required = new[] { "name" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return new { error = "name is required" };
        var crew = new Crew
        {
            Name = name!,
            Headcount = args.TryGetProperty("headcount", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 1,
            Skills = args.TryGetProperty("skills", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null,
            DayRateUsd = args.TryGetProperty("day_rate_usd", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDecimal() : 0
        };
        db.Crews.Add(crew);
        await db.SaveChangesAsync();
        return new { id = crew.Id, crew.Name, crew.Headcount, crew.DayRateUsd };
    }
}
