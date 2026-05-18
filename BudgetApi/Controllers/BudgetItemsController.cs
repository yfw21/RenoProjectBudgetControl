using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class BudgetItemsController : ControllerBase
    {
        private readonly BudgetContext _context;
        private readonly ILogger<BudgetItemsController> _log;
        public BudgetItemsController(BudgetContext context, ILogger<BudgetItemsController> log)
        {
            _context = context;
            _log = log;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<BudgetItem>>> GetBudgetItems([FromQuery] int? projectId)
        {
            var q = _context.BudgetItems.AsQueryable();
            if (projectId.HasValue) q = q.Where(b => b.ProjectId == projectId.Value);
            return await q.ToListAsync();
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<BudgetItem>> GetBudgetItem(int id)
        {
            var item = await _context.BudgetItems.FindAsync(id);
            if (item == null) return NotFound();
            return item;
        }

        [HttpPost]
        public async Task<ActionResult<BudgetItem>> PostBudgetItem(BudgetItem item)
        {
            _context.BudgetItems.Add(item);
            await _context.SaveChangesAsync();

            // Auto-bump % complete on the linked budget line: payment is a floor for completion.
            _log.LogInformation("BUMP-CHECK: item.Id={Id} BudgetLineId={LineId} Actual={Actual}",
                item.Id, item.BudgetLineId, item.Actual);
            if (item.BudgetLineId.HasValue && item.Actual > 0)
            {
                try
                {
                    var line = await _context.BudgetItems.FindAsync(item.BudgetLineId.Value);
                    _log.LogInformation("BUMP: line found={Found} budget={Budget} currentPct={Pct}",
                        line != null, line?.Budget, line?.PercentComplete);
                    if (line != null && line.Budget > 0)
                    {
                        var actuals = await _context.BudgetItems
                            .Where(b => b.BudgetLineId == line.Id)
                            .Select(b => b.Actual)
                            .ToListAsync();
                        var totalSpent = actuals.Sum();
                        var pctFromPayment = (int)Math.Min(100, Math.Round((double)(totalSpent / line.Budget) * 100));
                        _log.LogInformation("BUMP: totalSpent={Spent} pctFromPayment={Pct}",
                            totalSpent, pctFromPayment);
                        if (pctFromPayment > line.PercentComplete)
                        {
                            line.PercentComplete = pctFromPayment;
                            await _context.SaveChangesAsync();
                            _log.LogInformation("BUMP: applied — line {LineId} now at {Pct}%", line.Id, pctFromPayment);
                        }
                    }
                }
                catch (Exception e)
                {
                    _log.LogError(e, "BUMP failed for line {LineId}", item.BudgetLineId);
                }
            }
            return CreatedAtAction(nameof(GetBudgetItem), new { id = item.Id }, item);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> PutBudgetItem(int id, BudgetItem item)
        {
            if (id != item.Id) return BadRequest();
            _context.Entry(item).State = EntityState.Modified;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.BudgetItems.Any(e => e.Id == id))
                    return NotFound();
                else
                    throw;
            }
            return NoContent();
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteBudgetItem(int id)
        {
            var item = await _context.BudgetItems.FindAsync(id);
            if (item == null) return NotFound();
            _context.BudgetItems.Remove(item);
            await _context.SaveChangesAsync();
            return NoContent();
        }

        [HttpPost("{id}/receipt")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> AttachReceipt(int id, IFormFile file, IConfiguration cfg)
        {
            if (file == null || file.Length == 0) return BadRequest(new { error = "file required" });
            var item = await _context.BudgetItems.FindAsync(id);
            if (item == null) return NotFound();

            var storeRoot = cfg["Files:Root"] ?? Path.Combine(AppContext.BaseDirectory, "uploads");
            Directory.CreateDirectory(storeRoot);
            var safeName = $"{Guid.NewGuid():N}_{Path.GetFileName(file.FileName)}";
            var fullPath = Path.Combine(storeRoot, safeName);
            await using (var fs = System.IO.File.Create(fullPath))
                await file.CopyToAsync(fs);

            var blob = new FileBlob
            {
                ProjectId = item.ProjectId,
                FileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                StoragePath = fullPath,
                SizeBytes = file.Length,
                Kind = "receipt"
            };
            _context.FileBlobs.Add(blob);
            await _context.SaveChangesAsync();
            item.ReceiptFileBlobId = blob.Id;
            await _context.SaveChangesAsync();
            return Ok(new { receiptFileBlobId = blob.Id, fileName = blob.FileName });
        }

        [HttpDelete("{id}/receipt")]
        public async Task<IActionResult> DetachReceipt(int id)
        {
            var item = await _context.BudgetItems.FindAsync(id);
            if (item == null) return NotFound();
            item.ReceiptFileBlobId = null;
            await _context.SaveChangesAsync();
            return NoContent();
        }
    }
}
