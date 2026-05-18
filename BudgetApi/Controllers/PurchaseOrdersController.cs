using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/purchase-orders")]
public class PurchaseOrdersController : ControllerBase
{
    private readonly BudgetContext _db;
    public PurchaseOrdersController(BudgetContext db) { _db = db; }

    public record POLineDto(int MaterialId, string Material, decimal Quantity, decimal UnitPriceUsd, decimal? ReceivedQuantity);
    public record PODto(int Id, string Vendor, int DestinationProjectId, string Project, DateTime ExpectedDate,
        DateTime? ActualDate, string Status, string? Notes, List<POLineDto> Items, decimal TotalUsd);

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? projectId, [FromQuery] string? status)
    {
        var q = _db.PurchaseOrders.AsQueryable();
        if (projectId.HasValue) q = q.Where(p => p.DestinationProjectId == projectId);
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(p => p.Status == status);
        var pos = await q.OrderByDescending(p => p.ExpectedDate).ToListAsync();
        var projects = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name);
        var mats = await _db.Materials.ToDictionaryAsync(m => m.Id);
        var lines = await _db.PurchaseOrderItems
            .Where(i => pos.Select(p => p.Id).Contains(i.PurchaseOrderId)).ToListAsync();

        return Ok(pos.Select(p =>
        {
            var items = lines.Where(l => l.PurchaseOrderId == p.Id).Select(l => new POLineDto(
                l.MaterialId,
                mats.TryGetValue(l.MaterialId, out var mat) ? mat.Name : $"#{l.MaterialId}",
                l.Quantity, l.UnitPriceUsd, l.ReceivedQuantity)).ToList();
            return new PODto(p.Id, p.Vendor, p.DestinationProjectId,
                projects.TryGetValue(p.DestinationProjectId, out var pn) ? pn : $"#{p.DestinationProjectId}",
                p.ExpectedDate, p.ActualDate, p.Status, p.Notes, items,
                items.Sum(i => i.Quantity * i.UnitPriceUsd));
        }));
    }

    public record CreatePoLineRequest(int MaterialId, decimal Quantity, decimal UnitPriceUsd);
    public record CreatePoRequest(string Vendor, int DestinationProjectId, DateTime ExpectedDate,
        string? Notes, List<CreatePoLineRequest> Items);

    [HttpPost]
    public async Task<IActionResult> Create(CreatePoRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Vendor)) return BadRequest(new { error = "vendor required" });
        if (req.Items == null || req.Items.Count == 0) return BadRequest(new { error = "at least one item required" });

        var po = new PurchaseOrder
        {
            Vendor = req.Vendor,
            DestinationProjectId = req.DestinationProjectId,
            ExpectedDate = req.ExpectedDate,
            Notes = req.Notes,
            Status = "ordered"
        };
        _db.PurchaseOrders.Add(po);
        await _db.SaveChangesAsync();

        foreach (var line in req.Items)
        {
            _db.PurchaseOrderItems.Add(new PurchaseOrderItem
            {
                PurchaseOrderId = po.Id,
                MaterialId = line.MaterialId,
                Quantity = line.Quantity,
                UnitPriceUsd = line.UnitPriceUsd
            });
        }
        await _db.SaveChangesAsync();
        return Ok(new { id = po.Id });
    }

    public record ReceiveRequest(DateTime? ActualDate, Dictionary<int, decimal>? ShortItems);

    [HttpPost("{id}/receive")]
    public async Task<IActionResult> Receive(int id, ReceiveRequest req)
    {
        var po = await _db.PurchaseOrders.FindAsync(id);
        if (po == null) return NotFound();
        if (po.ActualDate != null) return BadRequest(new { error = "already received" });

        var items = await _db.PurchaseOrderItems.Where(i => i.PurchaseOrderId == id).ToListAsync();
        foreach (var it in items)
        {
            it.ReceivedQuantity = req.ShortItems != null && req.ShortItems.TryGetValue(it.MaterialId, out var q)
                ? q : it.Quantity;
            var stock = await _db.StockLevels.FirstOrDefaultAsync(s =>
                s.ProjectId == po.DestinationProjectId && s.MaterialId == it.MaterialId);
            if (stock == null)
            {
                stock = new StockLevel { ProjectId = po.DestinationProjectId, MaterialId = it.MaterialId };
                _db.StockLevels.Add(stock);
            }
            stock.Quantity += it.ReceivedQuantity ?? 0;
            stock.UpdatedAt = DateTime.UtcNow;
        }
        po.ActualDate = req.ActualDate ?? DateTime.UtcNow.Date;
        po.Status = "received";
        await _db.SaveChangesAsync();
        return Ok();
    }
}
