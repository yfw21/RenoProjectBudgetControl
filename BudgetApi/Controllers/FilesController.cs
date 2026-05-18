using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Rag;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly BudgetContext _db;
    private readonly Embedder _embedder;
    private readonly IConfiguration _cfg;
    private readonly ILogger<FilesController> _log;

    public FilesController(BudgetContext db, Embedder embedder, IConfiguration cfg, ILogger<FilesController> log)
    {
        _db = db; _embedder = embedder; _cfg = cfg; _log = log;
    }

    [HttpPost]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload(IFormFile file, [FromForm] int? projectId, [FromForm] string? kind, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { error = "file required" });
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "0");
        var storeRoot = _cfg["Files:Root"] ?? Path.Combine(AppContext.BaseDirectory, "uploads");
        Directory.CreateDirectory(storeRoot);
        var safeName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
        var fullPath = Path.Combine(storeRoot, safeName);
        await using (var fs = System.IO.File.Create(fullPath))
            await file.CopyToAsync(fs, ct);

        var blob = new FileBlob
        {
            ProjectId = projectId,
            UploadedByUserId = userId,
            FileName = file.FileName,
            ContentType = file.ContentType ?? "application/octet-stream",
            StoragePath = fullPath,
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
            text = await System.IO.File.ReadAllTextAsync(fullPath, ct);
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
    public async Task<IActionResult> Get(int id)
    {
        var blob = await _db.FileBlobs.FindAsync(id);
        if (blob is null || !System.IO.File.Exists(blob.StoragePath)) return NotFound();
        return PhysicalFile(blob.StoragePath, blob.ContentType, blob.FileName);
    }
}
