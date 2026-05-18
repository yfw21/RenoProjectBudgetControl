using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/materials")]
public class MaterialsController : ControllerBase
{
    private readonly BudgetContext _db;
    public MaterialsController(BudgetContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Material>>> List() =>
        await _db.Materials.OrderBy(m => m.Name).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<Material>> Create(Material m)
    {
        _db.Materials.Add(m);
        await _db.SaveChangesAsync();
        return Ok(m);
    }

    [HttpGet("stock")]
    public async Task<IActionResult> Stock([FromQuery] int? projectId)
    {
        var q = _db.StockLevels.AsQueryable();
        if (projectId.HasValue) q = q.Where(s => s.ProjectId == projectId);
        var mats = await _db.Materials.ToDictionaryAsync(m => m.Id);
        var projects = await _db.Projects.ToDictionaryAsync(p => p.Id, p => p.Name);
        var rows = await q.ToListAsync();
        return Ok(rows.Select(s => new
        {
            projectId = s.ProjectId,
            project = projects.TryGetValue(s.ProjectId, out var pn) ? pn : "?",
            materialId = s.MaterialId,
            material = mats.TryGetValue(s.MaterialId, out var m) ? m.Name : "?",
            unit = mats.TryGetValue(s.MaterialId, out var m2) ? m2.Unit : "",
            quantity = s.Quantity,
            updatedAt = s.UpdatedAt
        }));
    }
}
