using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Llm;

namespace BudgetApi.Tools;

public class RecordDeliveryTool : ITool
{
    public string Name => "record_delivery";
    public string Description => "Record receipt of a purchase order. Updates stock levels and optionally records short deliveries.";
    public object ParametersSchema => new
    {
        type = "object",
        properties = new
        {
            purchase_order_id = new { type = "integer" },
            actual_date = new { type = "string", description = "ISO date received. Defaults to today." },
            short_items = new
            {
                type = "array",
                description = "Optional: items where the received quantity differs from ordered. Each {material, received_quantity}",
                items = new
                {
                    type = "object",
                    properties = new
                    {
                        material = new { type = "string" },
                        received_quantity = new { type = "number" }
                    },
                    required = new[] { "material", "received_quantity" }
                }
            }
        },
        required = new[] { "purchase_order_id" }
    };

    public async Task<object> ExecuteAsync(JsonElement args, ToolContext ctx)
    {
        var db = ctx.Services.GetRequiredService<BudgetContext>();
        if (!args.TryGetProperty("purchase_order_id", out var pid) || !pid.TryGetInt32(out var poId))
            return new { error = "purchase_order_id required" };

        var po = await db.PurchaseOrders.FindAsync(poId);
        if (po is null) return new { error = "purchase order not found" };
        if (po.ActualDate != null) return new { error = "already received" };

        var actual = args.TryGetProperty("actual_date", out var ad) && ad.ValueKind == JsonValueKind.String && DateTime.TryParse(ad.GetString(), out var d)
            ? d : DateTime.UtcNow.Date;

        var items = await db.PurchaseOrderItems.Where(i => i.PurchaseOrderId == poId).ToListAsync();
        var matsByName = await db.Materials.Where(m => items.Select(i => i.MaterialId).Contains(m.Id)).ToDictionaryAsync(m => m.Name, m => m.Id);
        var shortMap = new Dictionary<int, decimal>();
        if (args.TryGetProperty("short_items", out var si) && si.ValueKind == JsonValueKind.Array)
        {
            foreach (var s in si.EnumerateArray())
            {
                var name = s.TryGetProperty("material", out var n) ? n.GetString() : null;
                var qty = s.TryGetProperty("received_quantity", out var rq) && rq.ValueKind == JsonValueKind.Number ? rq.GetDecimal() : 0;
                if (name != null && matsByName.TryGetValue(name, out var mid)) shortMap[mid] = qty;
            }
        }
        foreach (var it in items)
        {
            it.ReceivedQuantity = shortMap.TryGetValue(it.MaterialId, out var rq) ? rq : it.Quantity;
            var stock = await db.StockLevels.FirstOrDefaultAsync(s => s.ProjectId == po.DestinationProjectId && s.MaterialId == it.MaterialId);
            if (stock is null)
            {
                stock = new StockLevel { ProjectId = po.DestinationProjectId, MaterialId = it.MaterialId, Quantity = 0 };
                db.StockLevels.Add(stock);
            }
            stock.Quantity += it.ReceivedQuantity ?? 0;
            stock.UpdatedAt = DateTime.UtcNow;
        }
        po.ActualDate = actual;
        po.Status = "received";
        await db.SaveChangesAsync();
        return new { id = po.Id, vendor = po.Vendor, received_on = actual.ToString("yyyy-MM-dd") };
    }
}
