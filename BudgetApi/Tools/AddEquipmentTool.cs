using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AddEquipmentTool : ITool
{
    public string Name => "add_equipment";
    public string Description => "Add a piece of equipment (excavator, mixer, scaffolding, etc.) to the resource pool.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string", description = "Equipment identifier (required)" },
            type = new { type = "string", description = "Equipment type (e.g. excavator)" },
            day_rate_usd = new { type = "number" },
            rented = new { type = "boolean", description = "True if rented, false if owned" }
        },
        required = new[] { "name" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return new { error = "name is required" };
        var e = new Equipment
        {
            Name = name!,
            Type = args.TryGetProperty("type", out var t) && t.ValueKind == JsonValueKind.String ? t.GetString() : null,
            DayRateUsd = args.TryGetProperty("day_rate_usd", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetDecimal() : 0,
            Rented = args.TryGetProperty("rented", out var rn) && rn.ValueKind == JsonValueKind.True
        };
        db.Equipment.Add(e);
        await db.SaveChangesAsync();
        return new { id = e.Id, e.Name, e.Type, e.DayRateUsd, e.Rented };
    }
}
