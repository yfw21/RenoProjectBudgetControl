using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Rag;
using BudgetApi.Services.Storage;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly BudgetContext _db;
    private readonly Embedder _embedder;
    private readonly IFileStore _store;
    private readonly ILogger<FilesController> _log;

    public FilesController(BudgetContext db, Embedder embedder, IFileStore store, ILogger<FilesController> log)
    {
        _db = db; _embedder = embedder; _store = store; _log = log;
    }

    [HttpPost]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] int? projectId, [FromForm] string? kind, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "file required" });
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        var key = await _store.SaveAsync(file, ct);

        var blob = new FileBlob
        {
            ProjectId = projectId,
            UploadedByUserId = userId,
            FileName = file.FileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            StoragePath = key,
            SizeBytes = file.Length,
            Kind = kind ?? "other"
        };
        _db.FileBlobs.Add(blob);
        await _db.SaveChangesAsync(ct);

        string? text = null;
        if (file.ContentType?.StartsWith("text/") == true ||
            file.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            file.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(file.OpenReadStream());
            text = await reader.ReadToEndAsync(ct);
        }

        Note? note = null;
        if (!string.IsNullOrWhiteSpace(text))
        {
            note = new Note { ProjectId = projectId, Text = text!, Source = "file", FileBlobId = blob.Id };
            try { note.Embedding = Embedder.Serialize(await _embedder.EmbedAsync(text!, ct)); }
            catch (Exception e) { _log.LogWarning("Embedding failed: {Msg}", e.Message); }
            _db.Notes.Add(note);
            await _db.SaveChangesAsync(ct);
        }

        return Ok(new { id = blob.Id, blob.FileName, blob.Kind, indexed = note != null });
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> Get(int id, CancellationToken ct)
    {
        var blob = await _db.FileBlobs.FindAsync(new object?[] { id }, ct);
        if (blob is null) return NotFound();
        var stream = await _store.OpenReadAsync(blob.StoragePath, ct);
        if (stream is null) return NotFound();
        return File(stream, blob.ContentType, blob.FileName);
    }
}
