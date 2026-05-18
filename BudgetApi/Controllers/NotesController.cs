using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/notes")]
public class NotesController : ControllerBase
{
    private readonly BudgetContext _db;
    public NotesController(BudgetContext db) { _db = db; }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? projectId)
    {
        var q = _db.Notes.AsQueryable();
        if (projectId.HasValue) q = q.Where(n => n.ProjectId == projectId);
        var notes = await q.OrderByDescending(n => n.CreatedAt).Take(200)
            .Select(n => new { n.Id, n.ProjectId, n.Text, n.Source, n.FileBlobId, n.CreatedAt }).ToListAsync();
        return Ok(notes);
    }

    public record AddNoteRequest(int? ProjectId, string Text);

    [HttpPost]
    public async Task<IActionResult> Create(AddNoteRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Text)) return BadRequest(new { error = "text required" });
        var note = new Note { ProjectId = req.ProjectId, Text = req.Text, Source = "user" };
        _db.Notes.Add(note);
        await _db.SaveChangesAsync();
        return Ok(new { id = note.Id });
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var n = await _db.Notes.FindAsync(id);
        if (n == null) return NotFound();
        _db.Notes.Remove(n);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
