using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;

namespace BudgetApi.Services.Materials;

public class BurnRateService
{
    private readonly BudgetContext _db;
    public BurnRateService(BudgetContext db) { _db = db; }

    public async Task<decimal> AvgDailyBurnAsync(int projectId, int materialId, int lookbackDays = 30)
    {
        var since = DateTime.UtcNow.Date.AddDays(-lookbackDays);
        var totalReceived = await (
            from po in _db.PurchaseOrders
            where po.DestinationProjectId == projectId && po.ActualDate != null && po.ActualDate >= since
            join item in _db.PurchaseOrderItems on po.Id equals item.PurchaseOrderId
            where item.MaterialId == materialId
            select item.ReceivedQuantity ?? item.Quantity
        ).SumAsync();

        if (lookbackDays <= 0) return 0;
        return totalReceived / lookbackDays;
    }

    public async Task<DateTime?> EstimateRunOutDateAsync(int projectId, int materialId, int lookbackDays = 30)
    {
        var stock = await _db.StockLevels
            .Where(s => s.ProjectId == projectId && s.MaterialId == materialId)
            .Select(s => (decimal?)s.Quantity).FirstOrDefaultAsync() ?? 0m;
        var burn = await AvgDailyBurnAsync(projectId, materialId, lookbackDays);
        if (burn <= 0) return null;
        var daysLeft = (double)(stock / burn);
        return DateTime.UtcNow.Date.AddDays(daysLeft);
    }
}
