using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/equipment")]
public class EquipmentController : ControllerBase
{
    private readonly BudgetContext _db;
    public EquipmentController(BudgetContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Equipment>>> List() =>
        await _db.Equipment.OrderBy(e => e.Name).ToListAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<Equipment>> Get(int id) =>
        await _db.Equipment.FindAsync(id) is { } e ? e : NotFound();

    [HttpPost]
    public async Task<ActionResult<Equipment>> Create(Equipment e)
    {
        _db.Equipment.Add(e);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = e.Id }, e);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Equipment e)
    {
        if (id != e.Id) return BadRequest();
        _db.Entry(e).State = EntityState.Modified;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var e = await _db.Equipment.FindAsync(id);
        if (e == null) return NotFound();
        _db.Equipment.Remove(e);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
