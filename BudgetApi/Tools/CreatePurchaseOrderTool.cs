using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class CreatePurchaseOrderTool : ITool
{
    public string Name => "create_purchase_order";
    public string Description => "Create a purchase order for materials, destined for a specific project.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            vendor = new { type = "string" },
            project = new { type = "string", description = "Destination project name or id" },
            expected_date = new { type = "string", description = "ISO date when delivery is expected" },
            items = new
            {
                type = "array",
                description = "Line items: each {material, quantity, unit_price_usd}",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        material = new { type = "string" },
                        quantity = new { type = "number" },
                        unit_price_usd = new { type = "number" }
                    },
                    required = new[] { "material", "quantity" }
                }
            },
            notes = new { type = "string" }
        },
        required = new[] { "vendor", "project", "expected_date", "items" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        var project = await ProjectResolver.ResolveAsync(db, args);
        if (project is null) return new { error = "project not found" };

        var vendor = args.TryGetProperty("vendor", out var v) ? v.GetString() : null;
        if (string.IsNullOrWhiteSpace(vendor)) return new { error = "vendor required" };

        if (!args.TryGetProperty("expected_date", out var ed) || !DateTime.TryParse(ed.GetString(), out var expected))
            return new { error = "valid expected_date required" };

        if (!args.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array || items.GetArrayLength() == 0)
            return new { error = "items required" };

        var po = new PurchaseOrder
        {
            Vendor = vendor!,
            DestinationProjectId = project.Id,
            ExpectedDate = expected,
            Notes = args.TryGetProperty("notes", out var nt) && nt.ValueKind == JsonValueKind.String ? nt.GetString() : null,
            Status = "ordered"
        };
        db.PurchaseOrders.Add(po);
        await db.SaveChangesAsync();

        var lines = new List<object>();
        foreach (var it in items.EnumerateArray())
        {
            var matName = it.TryGetProperty("material", out var mn) ? mn.GetString() : null;
            if (string.IsNullOrWhiteSpace(matName)) continue;
            var mat = await db.Materials.FirstOrDefaultAsync(x => x.Name == matName);
            if (mat is null) { mat = new Material { Name = matName!, Unit = "ea" }; db.Materials.Add(mat); await db.SaveChangesAsync(); }
            var qty = it.TryGetProperty("quantity", out var q) && q.ValueKind == JsonValueKind.Number ? q.GetDecimal() : 0;
            var price = it.TryGetProperty("unit_price_usd", out var up) && up.ValueKind == JsonValueKind.Number ? up.GetDecimal() : (mat.DefaultUnitPriceUsd ?? 0);
            db.PurchaseOrderItems.Add(new PurchaseOrderItem { PurchaseOrderId = po.Id, MaterialId = mat.Id, Quantity = qty, UnitPriceUsd = price });
            lines.Add(new { material = mat.Name, quantity = qty, unit_price_usd = price, line_total_usd = qty * price });
        }
        await db.SaveChangesAsync();
        var total = lines.Sum(l => (decimal)((dynamic)l).line_total_usd);
        return new { id = po.Id, vendor = po.Vendor, project = project.Name, expected = expected.ToString("yyyy-MM-dd"), items = lines, total_usd = total };
    }
}
