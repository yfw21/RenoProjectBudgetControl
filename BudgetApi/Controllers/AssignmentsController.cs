using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/assignments")]
public class AssignmentsController : ControllerBase
{
    private readonly BudgetContext _db;
    public AssignmentsController(BudgetContext db) { _db = db; }

    public record AssignmentDto(int Id, int TaskId, string TaskName, string ResourceKind,
        int ResourceId, string ResourceName, DateTime Start, DateTime End);

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AssignmentDto>>> List([FromQuery] int? projectId)
    {
        var q = _db.Assignments.AsQueryable();
        if (projectId.HasValue)
        {
            var taskIds = await _db.CategoryActivities
                .Where(t => t.ProjectId == projectId)
                .Select(t => t.Id).ToListAsync();
            q = q.Where(a => taskIds.Contains(a.TaskId));
        }
        var assignments = await q.OrderBy(a => a.Start).ToListAsync();
        var tasks = await _db.CategoryActivities.ToDictionaryAsync(t => t.Id, t => t.Name);
        var crews = await _db.Crews.ToDictionaryAsync(c => c.Id, c => c.Name);
        var equip = await _db.Equipment.ToDictionaryAsync(e => e.Id, e => e.Name);

        return assignments.Select(a => new AssignmentDto(
            a.Id, a.TaskId,
            tasks.TryGetValue(a.TaskId, out var tn) ? tn : $"task#{a.TaskId}",
            a.ResourceKind.ToString().ToLower(),
            a.ResourceId,
            a.ResourceKind == ResourceKind.Crew
                ? (crews.TryGetValue(a.ResourceId, out var cn) ? cn : $"crew#{a.ResourceId}")
                : (equip.TryGetValue(a.ResourceId, out var en) ? en : $"equipment#{a.ResourceId}"),
            a.Start, a.End)).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<Assignment>> Create(Assignment a)
    {
        _db.Assignments.Add(a);
        await _db.SaveChangesAsync();
        return Ok(a);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var a = await _db.Assignments.FindAsync(id);
        if (a == null) return NotFound();
        _db.Assignments.Remove(a);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
