using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;
using BudgetApi.Services.Storage;

namespace BudgetApi.Controllers
{
    [ApiController]
    [Authorize]
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

        public record BatchItem(string Description, decimal Actual, string? Vendor, string? Date, string? Category, int? BudgetLineId);
        public record BatchRequest(int ProjectId, string? Vendor, string? Date, List<BatchItem> Items);

        /// <summary>
        /// Creates many expenses in one shot (used by the contractor-invoice flow where a single
        /// PDF maps to multiple budget lines). Optionally attaches a receipt file to ALL of them.
        /// Auto-bumps % complete once per affected budget line at the end.
        /// </summary>
        [HttpPost("batch")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> CreateBatch([FromForm] string payload, IFormFile? file, [FromServices] IFileStore store)
        {
            BatchRequest? req;
            try { req = System.Text.Json.JsonSerializer.Deserialize<BatchRequest>(payload,
                new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
            catch (Exception e) { return BadRequest(new { error = $"bad payload: {e.Message}" }); }
            if (req == null || req.Items == null || req.Items.Count == 0)
                return BadRequest(new { error = "items required" });

            int? receiptBlobId = null;
            if (file != null && file.Length > 0)
            {
                var key = await store.SaveAsync(file);
                var blob = new FileBlob
                {
                    ProjectId = req.ProjectId,
                    FileName = file.FileName,
                    ContentType = file.ContentType ?? "application/octet-stream",
                    StoragePath = key,
                    SizeBytes = file.Length,
                    Kind = "receipt"
                };
                _context.FileBlobs.Add(blob);
                await _context.SaveChangesAsync();
                receiptBlobId = blob.Id;
            }

            DateTime? parsedDate = null;
            if (!string.IsNullOrWhiteSpace(req.Date) && DateTime.TryParse(req.Date, out var dt))
                parsedDate = DateTime.SpecifyKind(dt, DateTimeKind.Utc);

            var created = new List<BudgetItem>();
            foreach (var it in req.Items)
            {
                DateTime? itemDate = parsedDate;
                if (!string.IsNullOrWhiteSpace(it.Date) && DateTime.TryParse(it.Date, out var itDt))
                    itemDate = DateTime.SpecifyKind(itDt, DateTimeKind.Utc);
                var item = new BudgetItem
                {
                    ProjectId = req.ProjectId,
                    Category = it.Category ?? "General",
                    Description = it.Description,
                    Budget = 0,
                    Actual = it.Actual,
                    Vendor = it.Vendor ?? req.Vendor,
                    Date = itemDate,
                    Status = "paid",
                    BudgetLineId = it.BudgetLineId,
                    ReceiptFileBlobId = receiptBlobId
                };
                _context.BudgetItems.Add(item);
                created.Add(item);
            }
            await _context.SaveChangesAsync();

            // Aggregate auto-bump: one update per affected line, not per expense.
            var affectedLineIds = created.Where(x => x.BudgetLineId.HasValue).Select(x => x.BudgetLineId!.Value).Distinct().ToList();
            foreach (var lineId in affectedLineIds)
            {
                var line = await _context.BudgetItems.FindAsync(lineId);
                if (line == null || line.Budget <= 0) continue;
                var actuals = await _context.BudgetItems.Where(b => b.BudgetLineId == lineId).Select(b => b.Actual).ToListAsync();
                var spent = actuals.Sum();
                var pct = (int)Math.Min(100, Math.Round((double)(spent / line.Budget) * 100));
                if (pct > line.PercentComplete)
                {
                    line.PercentComplete = pct;
                }
            }
            if (affectedLineIds.Count > 0) await _context.SaveChangesAsync();

            return Ok(new
            {
                createdIds = created.Select(c => c.Id).ToList(),
                receiptFileBlobId = receiptBlobId,
                affectedLineIds
            });
        }

        [HttpPost("{id}/receipt")]
        [RequestSizeLimit(20_000_000)]
        public async Task<IActionResult> AttachReceipt(int id, IFormFile file, [FromServices] IFileStore store)
        {
            if (file == null || file.Length == 0) return BadRequest(new { error = "file required" });
            var item = await _context.BudgetItems.FindAsync(id);
            if (item == null) return NotFound();

            var key = await store.SaveAsync(file);

            var blob = new FileBlob
            {
                ProjectId = item.ProjectId,
                FileName = file.FileName,
                ContentType = file.ContentType ?? "application/octet-stream",
                StoragePath = key,
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
