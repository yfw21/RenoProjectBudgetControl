using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/crews")]
public class CrewsController : ControllerBase
{
    private readonly BudgetContext _db;
    public CrewsController(BudgetContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Crew>>> List() =>
        await _db.Crews.OrderBy(c => c.Name).ToListAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<Crew>> Get(int id) =>
        await _db.Crews.FindAsync(id) is { } c ? c : NotFound();

    [HttpPost]
    public async Task<ActionResult<Crew>> Create(Crew c)
    {
        _db.Crews.Add(c);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = c.Id }, c);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Crew c)
    {
        if (id != c.Id) return BadRequest();
        _db.Entry(c).State = EntityState.Modified;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var c = await _db.Crews.FindAsync(id);
        if (c == null) return NotFound();
        _db.Crews.Remove(c);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
