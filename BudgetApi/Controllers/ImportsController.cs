using ClosedXML.Excel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BudgetApi.Data;
using BudgetApi.Models;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/imports")]
public class ImportsController : ControllerBase
{
    private readonly BudgetContext _db;
    public ImportsController(BudgetContext db) { _db = db; }

    public record ImportResult(int Inserted, int Skipped, List<string> Errors);

    [HttpPost("expenses")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportExpenses(IFormFile file, [FromForm] int projectId)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "file required" });
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return BadRequest(new { error = "project not found" });

        var errors = new List<string>();
        var inserted = 0;
        var skipped = 0;

        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var rows = ws.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();

        var existingCats = await _db.Categories.Select(c => c.Value).ToListAsync();
        var newCats = new HashSet<string>();

        foreach (var row in rows)
        {
            try
            {
                var desc = row.Cell(3).GetString().Trim();
                if (string.IsNullOrWhiteSpace(desc)) { skipped++; continue; }
                var dateStr = row.Cell(1).GetString().Trim();
                DateTime? date = DateTime.TryParse(dateStr, out var d) ? d : null;
                var category = row.Cell(2).GetString().Trim();
                if (string.IsNullOrWhiteSpace(category)) category = "General";
                var vendor = row.Cell(4).GetString().Trim();
                var amount = TryGetDecimal(row.Cell(5));
                var budget = TryGetDecimal(row.Cell(6));
                var status = row.Cell(7).GetString().Trim();

                if (!existingCats.Contains(category) && newCats.Add(category))
                    _db.Categories.Add(new Category { Value = category });

                _db.BudgetItems.Add(new BudgetItem
                {
                    ProjectId = projectId,
                    Category = category,
                    Description = desc,
                    Actual = amount,
                    Budget = budget,
                    Vendor = string.IsNullOrWhiteSpace(vendor) ? null : vendor,
                    Date = date,
                    Status = string.IsNullOrWhiteSpace(status) ? "paid" : status
                });
                inserted++;
            }
            catch (Exception e)
            {
                errors.Add($"Row {row.RowNumber()}: {e.Message}");
            }
        }
        await _db.SaveChangesAsync();
        return Ok(new ImportResult(inserted, skipped, errors));
    }

    [HttpPost("tasks")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportTasks(IFormFile file, [FromForm] int projectId)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "file required" });
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return BadRequest(new { error = "project not found" });

        var errors = new List<string>();
        var inserted = 0;
        var skipped = 0;

        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var rows = ws.RangeUsed()?.RowsUsed().Skip(1) ?? Enumerable.Empty<IXLRangeRow>();

        foreach (var row in rows)
        {
            try
            {
                var name = row.Cell(1).GetString().Trim();
                if (string.IsNullOrWhiteSpace(name)) { skipped++; continue; }
                var category = row.Cell(2).GetString().Trim();
                if (string.IsNullOrWhiteSpace(category)) category = "General";
                if (!DateTime.TryParse(row.Cell(3).GetString(), out var start))
                    throw new Exception("invalid Start date");
                if (!DateTime.TryParse(row.Cell(4).GetString(), out var end))
                    throw new Exception("invalid End date");
                if (end < start) throw new Exception("End before Start");
                var hours = (int)TryGetDecimal(row.Cell(5));
                if (hours <= 0) hours = (int)Math.Max(1, (end - start).TotalDays + 1) * 8;

                _db.CategoryActivities.Add(new CategoryActivity
                {
                    ProjectId = projectId,
                    Name = name,
                    CategoryValue = category,
                    StartDate = start,
                    EndDate = end,
                    WorkHours = hours
                });
                inserted++;
            }
            catch (Exception e)
            {
                errors.Add($"Row {row.RowNumber()}: {e.Message}");
            }
        }
        await _db.SaveChangesAsync();
        return Ok(new ImportResult(inserted, skipped, errors));
    }

    [HttpPost("budget")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ImportBudget(IFormFile file, [FromForm] int projectId, [FromForm] bool replaceExisting = false)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "file required" });
        var project = await _db.Projects.FindAsync(projectId);
        if (project == null) return BadRequest(new { error = "project not found" });

        var errors = new List<string>();
        var inserted = 0;
        var skipped = 0;
        var categoriesUsed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (replaceExisting)
        {
            var old = _db.BudgetItems.Where(b => b.ProjectId == projectId && b.Actual == 0);
            _db.BudgetItems.RemoveRange(old);
        }

        var existingCats = await _db.Categories.Select(c => c.Value.ToLower()).ToListAsync();
        var existingCatSet = new HashSet<string>(existingCats);

        using var stream = file.OpenReadStream();
        using var wb = new XLWorkbook(stream);
        var ws = wb.Worksheets.First();
        var range = ws.RangeUsed();
        if (range == null) return Ok(new ImportResult(0, 0, new() { "Empty worksheet" }));

        var (amountCol, adjustedCol, headerFound) = DetectAmountColumns(range);

        string? currentCategory = null;
        var columnHeaderSeen = false;

        foreach (var row in range.RowsUsed())
        {
            try
            {
                var c1 = row.Cell(1).GetString().Trim();
                var c2cell = row.Cell(2);
                var c2 = c2cell.GetString().Trim();

                // Skip totally blank rows
                if (string.IsNullOrEmpty(c1) && string.IsNullOrEmpty(c2)
                    && row.Cell(amountCol).IsEmpty() && row.Cell(adjustedCol).IsEmpty())
                    continue;

                // Skip TOTAL / GRAND TOTAL / SUBTOTAL rows — they're spreadsheet sums, not budget lines.
                if (LooksLikeTotalRow(c1, c2)) { skipped++; continue; }

                // Section-header rows have Description / Amount marker labels in cols 2-6
                if (IsSectionHeaderRow(row))
                {
                    if (!columnHeaderSeen && LooksLikeColumnHeader(c1))
                    {
                        columnHeaderSeen = true;
                        continue;
                    }
                    if (!string.IsNullOrEmpty(c1)) currentCategory = CleanCategory(c1);
                    continue;
                }

                if (LooksLikeColumnHeader(c1) || LooksLikeMeta(c1)) { skipped++; continue; }

                // Extract amount strictly from the Amount / Adjusted columns (no col-5+ leakage)
                var amount = AmountFromRow(row, amountCol, adjustedCol);

                // Text-only rows (no amount, no description) — treat as loose section header
                if (amount == 0)
                {
                    if (!string.IsNullOrEmpty(c1) && string.IsNullOrEmpty(c2))
                        currentCategory = CleanCategory(c1);
                    skipped++;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(c1)) { skipped++; continue; }

                var description = (!string.IsNullOrWhiteSpace(c2) && c2cell.DataType != XLDataType.Number)
                    ? $"{c1} — {c2}" : c1;

                var category = currentCategory ?? "Uncategorized";
                if (existingCatSet.Add(category.ToLower()) && categoriesUsed.Add(category))
                    _db.Categories.Add(new Category { Value = category });
                categoriesUsed.Add(category);

                _db.BudgetItems.Add(new BudgetItem
                {
                    ProjectId = projectId,
                    Category = category,
                    Description = description.Length > 500 ? description[..500] : description,
                    Budget = amount,
                    Actual = 0,
                    Status = "planned"
                });
                inserted++;
            }
            catch (Exception e)
            {
                errors.Add($"Row {row.RowNumber()}: {e.Message}");
            }
        }
        await _db.SaveChangesAsync();
        return Ok(new
        {
            inserted,
            skipped,
            categories = categoriesUsed.ToList(),
            amountColumn = amountCol,
            adjustedColumn = adjustedCol,
            errors
        });
    }

    private static (int amountCol, int adjustedCol, bool found) DetectAmountColumns(IXLRange range)
    {
        foreach (var row in range.RowsUsed().Take(20))
        {
            int? amount = null, adjusted = null, secondAmount = null;
            for (var c = 1; c <= 10; c++)
            {
                var text = row.Cell(c).GetString().Trim().ToUpperInvariant();
                if (text is "AMOUNT" or "PRICE" or "COST" or "TOTAL COST" or "BUDGET")
                {
                    if (amount.HasValue) secondAmount ??= c;
                    else amount = c;
                }
                else if (text is "ADJUSTED" or "ADJUSTED AMOUNT" or "REVISED" or "REVISED AMOUNT")
                    adjusted ??= c;
            }
            if (amount.HasValue)
                return (amount.Value, adjusted ?? secondAmount ?? amount.Value, true);
        }
        return (3, 4, false);
    }

    private static bool IsSectionHeaderRow(IXLRangeRow row)
    {
        var combined = new System.Text.StringBuilder();
        for (var c = 2; c <= 6; c++)
            combined.Append(row.Cell(c).GetString().ToUpperInvariant()).Append(' ');
        var s = combined.ToString();
        return s.Contains("DESCRIPTION") && (s.Contains("AMOUNT") || s.Contains("PRICE") || s.Contains("COST"));
    }

    private static bool LooksLikeTotalRow(string c1, string c2)
    {
        var n1 = c1.ToUpperInvariant().Trim().TrimEnd(':').Trim();
        var n2 = c2.ToUpperInvariant().Trim().TrimEnd(':').Trim();
        string[] totals = { "TOTAL", "GRAND TOTAL", "SUBTOTAL", "SUB-TOTAL", "SUB TOTAL", "TOTAL COST", "GRAND TOTAL COST" };
        return totals.Any(t => n1 == t) || totals.Any(t => n2 == t);
    }

    private static decimal AmountFromRow(IXLRangeRow row, int amountCol, int adjustedCol)
    {
        var adjCell = row.Cell(adjustedCol);
        if (!adjCell.IsEmpty()) return TryNumber(adjCell);
        return TryNumber(row.Cell(amountCol));
    }

    private static decimal TryNumber(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        if (cell.DataType == XLDataType.Number)
        {
            var d = cell.GetDouble();
            return double.IsFinite(d) ? (decimal)d : 0m;
        }
        var s = cell.GetString().Replace("$", "").Replace(",", "").Replace("(", "-").Replace(")", "").Trim();
        return decimal.TryParse(s, out var v) ? v : 0m;
    }

    private static bool LooksLikeColumnHeader(string s)
    {
        var n = s.ToUpperInvariant();
        return n is "BUDGET ITEM" or "DESCRIPTION" or "AMOUNT" or "TOTAL" or "GRAND TOTAL"
            or "ITEM" or "CATEGORY" or "ADJUSTED" or "QTY" or "QUANTITY" or "PRICE" or "UNIT"
            or "#VALUE!" or "NAME" or "SUBTOTAL" or "VENDOR" or "DATE";
    }

    private static bool LooksLikeMeta(string s)
    {
        var n = s.ToUpperInvariant();
        return n.StartsWith("SCOPE OF WORK") || n.StartsWith("PROPERTY ADDRESS")
            || n.StartsWith("THE INFORMATION") || n.StartsWith("BASIC LINE ITEM")
            || n.StartsWith("PROJECT ") || n.StartsWith("BORROWER");
    }

    private static string CleanCategory(string label)
    {
        var s = label.Trim();
        // Strip trailing colons or " - description"
        var idx = s.IndexOf(':');
        if (idx > 0) s = s[..idx];
        return s.Trim();
    }

    private static decimal TryGetDecimal(IXLCell cell)
    {
        if (cell.IsEmpty()) return 0;
        if (cell.DataType == XLDataType.Number) return (decimal)cell.GetDouble();
        var s = cell.GetString().Replace("$", "").Replace(",", "").Trim();
        return decimal.TryParse(s, out var v) ? v : 0;
    }
}
