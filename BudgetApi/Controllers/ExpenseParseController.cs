using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BudgetApi.Services.Receipts;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/expenses")]
public class ExpenseParseController : ControllerBase
{
    private readonly ReceiptExtractor _extractor;
    private readonly BudgetLineSuggester _suggester;
    public ExpenseParseController(ReceiptExtractor extractor, BudgetLineSuggester suggester)
    {
        _extractor = extractor; _suggester = suggester;
    }

    [HttpPost("parse")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Parse(IFormFile file, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "file required" });
        var result = await _extractor.ExtractAsync(file, ct);
        return Ok(ToDto(result));
    }

    public record QuickParseRequest(string Text);

    [HttpPost("parse/text")]
    public async Task<IActionResult> ParseText([FromBody] QuickParseRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Text)) return BadRequest(new { error = "text required" });
        var result = await _extractor.QuickParseAsync(req.Text, ct);
        return Ok(ToDto(result));
    }

    public record SuggestLineRequest(int ProjectId, string? Description, string? Vendor, string? Category);

    /// <summary>
    /// Asynchronous companion to /parse. Kept separate so the receipt-parse round trip stays fast;
    /// budget-line matching uses a larger reasoning model and can take ~30-90s on cold start.
    /// </summary>
    [HttpPost("suggest-line")]
    public async Task<IActionResult> SuggestLine([FromBody] SuggestLineRequest req, CancellationToken ct)
    {
        var suggestion = await _suggester.SuggestAsync(req.ProjectId, req.Description, req.Vendor, req.Category, ct);
        return Ok(new
        {
            budgetLineId = suggestion?.LineId,
            confidence = suggestion?.Confidence
        });
    }

    private static object ToDto(Services.Receipts.ExtractedExpense result) => new
    {
        amount = result.Amount,
        vendor = result.Vendor,
        description = result.Description,
        date = result.Date?.ToString("yyyy-MM-dd"),
        category = result.Category,
        missing = result.Missing,
        rawText = result.RawText,
        source = result.Source,
        note = result.Note
    };
}
