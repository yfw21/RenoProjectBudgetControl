using System.Text.Json;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class AddMaterialTool : ITool
{
    public string Name => "add_material";
    public string Description => "Register a material that can be ordered and tracked in stock (e.g. cement, rebar, tile).";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            name = new { type = "string" },
            unit = new { type = "string", description = "Unit of measure (bag, ton, m, m2, ea, etc.)" },
            default_unit_price_usd = new { type = "number" }
        },
        required = new[] { "name" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var name = args.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name)) return new { error = "name required" };
        var m = new Material
        {
            Name = name!,
            Unit = args.TryGetProperty("unit", out var u) && u.ValueKind == JsonValueKind.String ? (u.GetString() ?? "ea") : "ea",
            DefaultUnitPriceUsd = args.TryGetProperty("default_unit_price_usd", out var p) && p.ValueKind == JsonValueKind.Number ? p.GetDecimal() : null
        };
        db.Materials.Add(m);
        await db.SaveChangesAsync();
        return new { id = m.Id, m.Name, m.Unit };
    }
}
