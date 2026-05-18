using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using BudgetApi.Services.Receipts;

namespace BudgetApi.Controllers;

[ApiController]
[Authorize]
[Route("api/expenses/parse")]
public class ExpenseParseController : ControllerBase
{
    private readonly ReceiptExtractor _extractor;
    private readonly BudgetLineSuggester _suggester;
    public ExpenseParseController(ReceiptExtractor extractor, BudgetLineSuggester suggester)
    {
        _extractor = extractor; _suggester = suggester;
    }

    [HttpPost]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> Parse(IFormFile file, [FromForm] int? projectId, CancellationToken ct)
    {
        if (file == null || file.Length == 0) return BadRequest(new { error = "file required" });
        var result = await _extractor.ExtractAsync(file, ct);
        var suggestion = projectId.HasValue
            ? await _suggester.SuggestAsync(projectId.Value, result.Description, result.Vendor, result.Category, ct)
            : null;
        return Ok(ToDto(result, suggestion));
    }

    public record QuickParseRequest(string Text, int? ProjectId);

    [HttpPost("text")]
    public async Task<IActionResult> ParseText([FromBody] QuickParseRequest req, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(req?.Text)) return BadRequest(new { error = "text required" });
        var result = await _extractor.QuickParseAsync(req.Text, ct);
        var suggestion = req.ProjectId.HasValue
            ? await _suggester.SuggestAsync(req.ProjectId.Value, result.Description, null, result.Category, ct)
            : null;
        return Ok(ToDto(result, suggestion));
    }

    private static object ToDto(Services.Receipts.ExtractedExpense result, SuggestedLine? suggestion) => new
    {
        amount = result.Amount,
        vendor = result.Vendor,
        description = result.Description,
        date = result.Date?.ToString("yyyy-MM-dd"),
        category = result.Category,
        missing = result.Missing,
        rawText = result.RawText,
        source = result.Source,
        note = result.Note,
        budgetLineId = suggestion?.LineId,
        budgetLineConfidence = suggestion?.Confidence
    };
}
