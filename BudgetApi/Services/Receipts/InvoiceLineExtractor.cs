using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetApi.Services.Receipts;

public record ExtractedInvoiceLine(string Description, decimal? Quantity, decimal? UnitPrice, decimal Amount);

public record ExtractedInvoice(
    string? Vendor,
    string? InvoiceNumber,
    DateTime? InvoiceDate,
    decimal? Total,
    List<ExtractedInvoiceLine> Lines,
    string? RawText,
    string Source,
    string? Note);

/// <summary>
/// Extracts line items from a multi-line contractor invoice. Contrast with ReceiptExtractor,
/// which collapses a receipt to a single amount/vendor/description.
/// </summary>
public class InvoiceLineExtractor
{
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;
    private readonly TesseractOcr _ocr;
    private readonly ILogger<InvoiceLineExtractor> _log;

    public InvoiceLineExtractor(IConfiguration cfg, IHttpClientFactory httpFactory, TesseractOcr ocr, ILogger<InvoiceLineExtractor> log)
    {
        _cfg = cfg; _httpFactory = httpFactory; _ocr = ocr; _log = log;
    }

    public async Task<ExtractedInvoice> ExtractAsync(IFormFile file, CancellationToken ct)
    {
        var (text, source, note) = await GetTextAsync(file, ct);
        if (string.IsNullOrWhiteSpace(text))
            return new ExtractedInvoice(null, null, null, null, new(), null, source,
                note ?? "Could not extract text from the invoice.");

        try
        {
            var dto = await OllamaExtract(text, ct);
            if (dto is null)
                return new ExtractedInvoice(null, null, null, null, new(), TrimRaw(text), source,
                    "LLM did not return structured data.");
            return Finalize(dto, text, source, note);
        }
        catch (Exception e)
        {
            _log.LogWarning("Invoice extraction failed: {Msg}", e.Message);
            return new ExtractedInvoice(null, null, null, null, new(), TrimRaw(text), source,
                $"AI extraction failed: {e.Message}");
        }
    }

    private async Task<(string? text, string source, string? note)> GetTextAsync(IFormFile file, CancellationToken ct)
    {
        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();

        if (contentType == "application/pdf" || ext == ".pdf")
        {
            byte[] bytes;
            using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); bytes = ms.ToArray(); }

            string? text = null;
            try
            {
                using var pdfStream = new MemoryStream(bytes);
                text = PdfTextExtractor.Extract(pdfStream);
            }
            catch (Exception e)
            {
                _log.LogInformation("PDF text extraction threw ({Type}) — falling back to OCR", e.GetType().Name);
            }

            if (!string.IsNullOrWhiteSpace(text)) return (text, "pdf", null);

            if (_ocr.IsAvailable())
            {
                var png = PdfImageRenderer.RenderFirstPageToPng(bytes);
                if (png == null) return (null, "pdf+ocr", "Could not render PDF to image.");
                var ocrText = await _ocr.OcrAsync(png, ct);
                return (ocrText, "pdf+ocr", string.IsNullOrWhiteSpace(ocrText) ? "Tesseract returned no text." : null);
            }
            return (null, "pdf", "No OCR available — install tesseract or use a vision model.");
        }
        if (contentType.StartsWith("text/") || ext is ".txt" or ".md" or ".csv")
        {
            using var reader = new StreamReader(file.OpenReadStream());
            return (await reader.ReadToEndAsync(ct), "text", null);
        }
        if (contentType.StartsWith("image/") && _ocr.IsAvailable())
        {
            byte[] bytes;
            using (var ms = new MemoryStream()) { await file.CopyToAsync(ms, ct); bytes = ms.ToArray(); }
            return (await _ocr.OcrAsync(bytes, ct), "image+ocr", null);
        }
        return (null, "unknown", $"Unsupported file type: {contentType}.");
    }

    private const string SystemPrompt =
        "You extract billable line items from a contractor or vendor invoice. " +
        "Reply with VALID JSON ONLY in this exact shape:\n" +
        "{\"vendor\": \"<string|null>\", \"invoice_number\": \"<string|null>\", " +
        "\"invoice_date\": \"<YYYY-MM-DD|null>\", \"total\": <number|null>, " +
        "\"lines\": [{\"description\": \"<short phrase>\", \"quantity\": <number|null>, " +
        "\"unit_price\": <number|null>, \"amount\": <number>}]}\n\n" +
        "Rules:\n" +
        "- Include one entry in \"lines\" for each billable item on the invoice.\n" +
        "- Skip subtotal, tax, discount, and grand-total rows — those go in \"total\" only.\n" +
        "- \"amount\" is the line total (quantity × unit_price).\n" +
        "- \"description\" describes what was billed (work or material), NOT the vendor.\n" +
        "- The text may have OCR artifacts; make a best effort.";

    private async Task<LlmInvoiceDto?> OllamaExtract(string text, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(_cfg["Ollama:BaseUrl"] ?? "http://localhost:11434");
        // Invoice extraction needs to enumerate many lines; allow plenty of tokens and time.
        http.Timeout = TimeSpan.FromMinutes(5);
        var model = _cfg["Receipts:MatcherModel"]
            ?? _cfg["Receipts:Model"]
            ?? _cfg["Ollama:ChatModel"]
            ?? "llama3.1:8b";
        var body = new
        {
            model,
            stream = false,
            format = "json",
            options = new { temperature = 0.1, num_predict = 1500 },
            messages = new[] {
                new { role = "system", content = SystemPrompt },
                new { role = "user", content = $"--- Invoice text ---\n{text}\n--- End ---" }
            }
        };
        var resp = await http.PostAsJsonAsync("/api/chat", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"Ollama {(int)resp.StatusCode}: {json}");
        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        _log.LogDebug("Invoice LLM raw output: {Content}", content);
        return ParseLlmJson(content);
    }

    private static LlmInvoiceDto? ParseLlmJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var t = raw.Trim();
        var s = t.IndexOf('{');
        var e = t.LastIndexOf('}');
        if (s < 0 || e <= s) return null;
        try
        {
            return JsonSerializer.Deserialize<LlmInvoiceDto>(t[s..(e + 1)],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch { return null; }
    }

    private static ExtractedInvoice Finalize(LlmInvoiceDto dto, string text, string source, string? note)
    {
        DateTime? date = null;
        if (!string.IsNullOrWhiteSpace(dto.InvoiceDate) && DateTime.TryParse(dto.InvoiceDate, out var d)) date = d;
        var lines = (dto.Lines ?? new())
            .Where(l => l.Amount.HasValue && l.Amount.Value > 0 && !string.IsNullOrWhiteSpace(l.Description))
            .Select(l => new ExtractedInvoiceLine(
                l.Description!.Trim(),
                l.Quantity.HasValue ? (decimal?)l.Quantity : null,
                l.UnitPrice.HasValue ? (decimal?)l.UnitPrice : null,
                (decimal)l.Amount!.Value))
            .ToList();
        return new ExtractedInvoice(
            string.IsNullOrWhiteSpace(dto.Vendor) ? null : dto.Vendor!.Trim(),
            string.IsNullOrWhiteSpace(dto.InvoiceNumber) ? null : dto.InvoiceNumber!.Trim(),
            date,
            dto.Total.HasValue ? (decimal?)dto.Total : null,
            lines,
            TrimRaw(text),
            source,
            note);
    }

    private static string? TrimRaw(string text) => text.Length > 4000 ? text[..4000] + "…" : text;

    private class LlmInvoiceDto
    {
        [JsonPropertyName("vendor")] public string? Vendor { get; set; }
        [JsonPropertyName("invoice_number")] public string? InvoiceNumber { get; set; }
        [JsonPropertyName("invoice_date")] public string? InvoiceDate { get; set; }
        [JsonPropertyName("total")] public double? Total { get; set; }
        [JsonPropertyName("lines")] public List<LlmLineDto>? Lines { get; set; }
    }
    private class LlmLineDto
    {
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("quantity")] public double? Quantity { get; set; }
        [JsonPropertyName("unit_price")] public double? UnitPrice { get; set; }
        [JsonPropertyName("amount")] public double? Amount { get; set; }
    }
}
