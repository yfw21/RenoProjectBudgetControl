using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/projects")]
public class ProjectsController : ControllerBase
{
    private readonly BudgetContext _db;
    public ProjectsController(BudgetContext db) { _db = db; }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Project>>> List([FromQuery] string? status) =>
        await _db.Projects
            .Where(p => status == null || p.Status == status)
            .OrderByDescending(p => p.Id)
            .ToListAsync();

    [HttpGet("{id}")]
    public async Task<ActionResult<Project>> Get(int id) =>
        await _db.Projects.FindAsync(id) is { } p ? p : NotFound();

    [HttpPost]
    public async Task<ActionResult<Project>> Create(Project p)
    {
        _db.Projects.Add(p);
        await _db.SaveChangesAsync();
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(int id, Project p)
    {
        if (id != p.Id) return BadRequest();
        _db.Entry(p).State = EntityState.Modified;
        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var p = await _db.Projects.FindAsync(id);
        if (p == null) return NotFound();
        _db.Projects.Remove(p);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
