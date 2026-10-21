using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BudgetApi.Services.Receipts;

public record ExtractedExpense(
    decimal? Amount,
    string? Vendor,
    string? Description,
    DateTime? Date,
    string? Category,
    List<string> Missing,
    string? RawText,
    string Source,
    string? Note,
    IReadOnlyDictionary<string, float>? Confidence = null);

public class ReceiptExtractor
{
    private readonly IConfiguration _cfg;
    private readonly IHttpClientFactory _httpFactory;
    private readonly TesseractOcr _ocr;
    private readonly DocumentIntelligenceExtractor _docIntel;
    private readonly ILogger<ReceiptExtractor> _log;

    public ReceiptExtractor(IConfiguration cfg, IHttpClientFactory httpFactory, TesseractOcr ocr,
        DocumentIntelligenceExtractor docIntel, ILogger<ReceiptExtractor> log)
    {
        _cfg = cfg; _httpFactory = httpFactory; _ocr = ocr; _docIntel = docIntel; _log = log;
    }

    public async Task<ExtractedExpense> ExtractAsync(IFormFile file, CancellationToken ct)
    {
        string? azureNote = null;
        if (_docIntel.IsEnabled && DocumentIntelligenceExtractor.Supports(file.ContentType, file.FileName))
        {
            try
            {
                using var ms = new MemoryStream();
                await file.CopyToAsync(ms, ct);
                return await _docIntel.AnalyzeReceiptAsync(ms.ToArray(), ct);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                _log.LogWarning("Document Intelligence failed, falling back to local pipeline: {Msg}", e.Message);
                azureNote = "Azure Document Intelligence was unavailable; read with local OCR instead.";
            }
        }
        var local = await ExtractLocalAsync(file, ct);
        return azureNote == null ? local : local with { Note = local.Note == null ? azureNote : $"{azureNote} {local.Note}" };
    }

    private async Task<ExtractedExpense> ExtractLocalAsync(IFormFile file, CancellationToken ct)
    {
        var contentType = (file.ContentType ?? "").ToLowerInvariant();
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        var visionModel = _cfg["Receipts:VisionModel"];
        var ocrAvailable = _ocr.IsAvailable();

        string? text = null;
        string source;
        var notes = new List<string>();

        if (contentType == "application/pdf" || ext == ".pdf")
        {
            source = "pdf";
            byte[] pdfBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                pdfBytes = ms.ToArray();
            }
            try
            {
                using var pdfStream = new MemoryStream(pdfBytes);
                text = PdfTextExtractor.Extract(pdfStream);
            }
            catch (Exception e)
            {
                // Some retailer PDFs (e.g. Home Depot order printouts) have malformed xrefs
                // that crash PdfPig. Treat as no-text-found and let OCR/vision take over.
                _log.LogInformation("PDF text extraction threw ({Type}: {Msg}) — will try OCR/vision",
                    e.GetType().Name, e.Message);
                text = null;
            }

            if (string.IsNullOrWhiteSpace(text))
            {
                _log.LogInformation("PDF has no embedded text — falling back to OCR");
                var png = PdfImageRenderer.RenderFirstPageToPng(pdfBytes);
                if (png == null)
                {
                    notes.Add("Could not render PDF to image.");
                }
                else if (ocrAvailable)
                {
                    text = await _ocr.OcrAsync(png, ct);
                    if (string.IsNullOrWhiteSpace(text))
                        notes.Add("Tesseract returned no text.");
                    else
                        source = "pdf+ocr";
                }
                else if (!string.IsNullOrWhiteSpace(visionModel))
                {
                    var llmFromVision = await TryVisionExtract(png, visionModel!, ct);
                    if (llmFromVision != null) return Finalize(llmFromVision, null, "pdf+vision", null);
                    notes.Add($"Vision model '{visionModel}' returned no usable result.");
                }
                else
                {
                    notes.Add("No OCR available. Install tesseract or set Receipts:VisionModel.");
                }
            }
        }
        else if (contentType.StartsWith("text/") || ext is ".txt" or ".md" or ".csv")
        {
            using var reader = new StreamReader(file.OpenReadStream());
            text = await reader.ReadToEndAsync(ct);
            source = "text";
        }
        else if (contentType.StartsWith("image/"))
        {
            source = "image";
            byte[] imgBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms, ct);
                imgBytes = ms.ToArray();
            }

            if (ocrAvailable)
            {
                text = await _ocr.OcrAsync(imgBytes, ct);
                source = "image+ocr";
                if (string.IsNullOrWhiteSpace(text)) notes.Add("Tesseract returned no text from image.");
            }
            else if (!string.IsNullOrWhiteSpace(visionModel))
            {
                var llmFromVision = await TryVisionExtract(imgBytes, visionModel!, ct);
                if (llmFromVision != null) return Finalize(llmFromVision, null, "image+vision", null);
                notes.Add($"Vision model '{visionModel}' returned no usable result.");
            }
            else
            {
                notes.Add("Image receipts need tesseract or a vision model. Install tesseract-ocr or pull llava.");
            }
        }
        else
        {
            return new ExtractedExpense(null, null, null, null, null,
                new() { "amount", "vendor", "description" }, null, "unknown",
                $"Unsupported file type: {contentType}.");
        }

        if (string.IsNullOrWhiteSpace(text))
            return Finalize(null, null, source, string.Join(" ", notes));

        ExtractedExpense? llmResult = null;
        try
        {
            llmResult = await OllamaTextExtract(text, ct);
        }
        catch (Exception e)
        {
            _log.LogWarning("Ollama text extraction failed: {Msg}", e.Message);
            notes.Add($"AI extraction failed: {e.Message}");
        }
        return Finalize(llmResult, text, source, notes.Count > 0 ? string.Join(" ", notes) : null);
    }

    private async Task<ExtractedExpense?> TryVisionExtract(byte[] image, string visionModel, CancellationToken ct)
    {
        try { return await OllamaVisionExtract(image, visionModel, ct); }
        catch (Exception e) { _log.LogWarning("Vision extraction failed: {Msg}", e.Message); return null; }
    }

    private ExtractedExpense Finalize(ExtractedExpense? llm, string? text, string source, string? note)
    {
        var regex = !string.IsNullOrWhiteSpace(text) ? ReceiptParser.Parse(text!) : new ParsedReceipt(null, null, null);

        var amount = llm?.Amount ?? regex.Amount;
        var vendor = !string.IsNullOrWhiteSpace(llm?.Vendor) ? llm.Vendor : regex.Vendor;
        var description = llm?.Description;
        var date = llm?.Date ?? regex.Date;
        var category = llm?.Category;

        var missing = new List<string>();
        if (amount is null or 0) missing.Add("amount");
        if (string.IsNullOrWhiteSpace(vendor)) missing.Add("vendor");
        if (string.IsNullOrWhiteSpace(description)) missing.Add("description");

        var raw = text;
        if (raw != null && raw.Length > 2000) raw = raw[..2000] + "…";

        return new ExtractedExpense(amount, vendor, description, date, category, missing, raw, source,
            string.IsNullOrWhiteSpace(note) ? null : note);
    }

    private const string ExtractionSystemPrompt = """
        You extract structured fields from a receipt or invoice.
        Reply with VALID JSON ONLY, no commentary, in this exact shape:
        {
          "amount_usd": <number or null>,
          "vendor": "<string or null>",
          "description": "<short string describing what was bought, 3-8 words, NOT the vendor name>",
          "date": "<YYYY-MM-DD string or null>",
          "category": "<one of: Materials, Labor, Equipment Rental, Utilities, Fuel, Permits, Other>"
        }

        Rules:
        - amount_usd is the FINAL TOTAL paid, including tax — never subtotal, never tax alone, never a single line item.
        - vendor is the merchant or business name (usually near the top).
        - description summarizes what was bought as a phrase: e.g. "5 bags cement and rebar", "lunch for crew", "scaffolding rental 3 days". Never include the vendor.
        - date is the transaction date in YYYY-MM-DD format.
        - category must be one of the listed values. Use "Other" if unsure.
        - The text may have OCR artifacts (mis-recognized characters, extra spaces, broken lines). Make a best effort.

        Example input:
        WALMART
        1234 Main St
        05/15/2026
        2x Cement 50kg    24.00
        1x Hammer         15.50
        Subtotal          39.50
        Tax               3.16
        TOTAL             42.66

        Example output:
        {"amount_usd": 42.66, "vendor": "Walmart", "description": "2 bags cement and hammer", "date": "2026-05-15", "category": "Materials"}
        """;

    private const string QuickParseSystemPrompt = """
        You extract structured fields from a brief spoken or typed expense entry.
        Reply with VALID JSON ONLY, no commentary, in this exact shape:
        {
          "amount_usd": <number or null>,
          "description": "<short 2-5 word phrase of what was spent on>",
          "vendor": "<string or null — only if explicitly named>",
          "date": "<YYYY-MM-DD or null>",
          "category": "<one of: Materials, Labor, Equipment Rental, Utilities, Fuel, Permits, Transport, Other>"
        }

        Rules:
        - If the user says "today", "yesterday", "last Monday", resolve to YYYY-MM-DD using "Today is" context.
        - If no date is mentioned, return null for date.
        - Category guidance:
          * cleanup, labor, workers, crew, helper → Labor
          * cement, wood, paint, rebar, tile, nails, lumber → Materials
          * rental, rent → Equipment Rental
          * gas, diesel, fuel → Fuel
          * permit, license, inspection → Permits
          * taxi, uber, truck delivery → Transport
        - description is the activity/item, NOT the vendor.

        Examples:
        Input: "Today is 2026-05-15. User said: I spent 100 for clean up today"
        Output: {"amount_usd": 100, "description": "clean up", "vendor": null, "date": "2026-05-15", "category": "Labor"}

        Input: "Today is 2026-05-15. User said: 150 dollars for cement bags from Walmart"
        Output: {"amount_usd": 150, "description": "cement bags", "vendor": "Walmart", "date": null, "category": "Materials"}

        Input: "Today is 2026-05-15. User said: paid the electrician 800 yesterday"
        Output: {"amount_usd": 800, "description": "electrician work", "vendor": null, "date": "2026-05-14", "category": "Labor"}
        """;

    public async Task<ExtractedExpense> QuickParseAsync(string userText, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userText))
            return Finalize(null, null, "voice", "Empty input.");

        var today = DateTime.Now.ToString("yyyy-MM-dd");
        var userMessage = $"Today is {today}. User said: {userText.Trim()}";

        ExtractedExpense? llmResult = null;
        string? note = null;
        try
        {
            llmResult = await OllamaQuickExtract(userMessage, ct);
        }
        catch (Exception e)
        {
            _log.LogWarning("Quick parse failed: {Msg}", e.Message);
            note = $"AI extraction failed: {e.Message}";
        }
        return Finalize(llmResult, userText, "voice", note);
    }

    private async Task<ExtractedExpense?> OllamaQuickExtract(string userMessage, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(OllamaBaseUrl());
        http.Timeout = TimeSpan.FromMinutes(2);
        var model = ReceiptsModel();
        var body = new
        {
            model,
            stream = false,
            format = "json",
            options = new { temperature = 0.1, num_predict = 256 },
            messages = new[] {
                new { role = "system", content = QuickParseSystemPrompt },
                new { role = "user", content = userMessage }
            }
        };
        var resp = await http.PostAsJsonAsync("/api/chat", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        _log.LogInformation("Quick-parse LLM raw output: {Content}", content);
        return ParseLlmJson(content);
    }

    private string ReceiptsModel() =>
        _cfg["Receipts:Model"] ?? _cfg["Ollama:ChatModel"] ?? "llama3.1:8b";

    private string OllamaBaseUrl() =>
        _cfg["Ollama:BaseUrl"] ?? "http://localhost:11434";

    private async Task<ExtractedExpense?> OllamaTextExtract(string text, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(OllamaBaseUrl());
        http.Timeout = TimeSpan.FromMinutes(3);
        var model = ReceiptsModel();
        var body = new
        {
            model,
            stream = false,
            format = "json",
            options = new { temperature = 0.1, num_predict = 512 },
            messages = new[] {
                new { role = "system", content = ExtractionSystemPrompt },
                new { role = "user", content = $"--- Receipt text ---\n{text}\n--- End ---\n\nExtract the fields as JSON." }
            }
        };
        var resp = await http.PostAsJsonAsync("/api/chat", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        _log.LogInformation("Receipt LLM raw output: {Content}", content);
        return ParseLlmJson(content);
    }

    private async Task<ExtractedExpense?> OllamaVisionExtract(byte[] image, string model, CancellationToken ct)
    {
        var http = _httpFactory.CreateClient();
        http.BaseAddress = new Uri(OllamaBaseUrl());
        http.Timeout = TimeSpan.FromMinutes(5);
        var body = new
        {
            model,
            stream = false,
            format = "json",
            options = new { temperature = 0.1, num_predict = 512 },
            messages = new[] {
                new {
                    role = "user",
                    content = ExtractionSystemPrompt + "\n\nExtract the fields from the attached receipt image. Reply with the JSON only.",
                    images = new[] { Convert.ToBase64String(image) }
                }
            }
        };
        var resp = await http.PostAsJsonAsync("/api/chat", body, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException($"Ollama vision {(int)resp.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var content = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";
        _log.LogInformation("Receipt vision raw output: {Content}", content);
        return ParseLlmJson(content);
    }

    private class LlmExtractDto
    {
        [JsonPropertyName("amount_usd")] public double? AmountUsd { get; set; }
        [JsonPropertyName("vendor")] public string? Vendor { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("date")] public string? Date { get; set; }
        [JsonPropertyName("category")] public string? Category { get; set; }
    }

    private static ExtractedExpense? ParseLlmJson(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var trimmed = raw.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        var jsonText = trimmed[start..(end + 1)];
        try
        {
            var dto = JsonSerializer.Deserialize<LlmExtractDto>(jsonText,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (dto is null) return null;
            DateTime? date = null;
            if (!string.IsNullOrWhiteSpace(dto.Date) && DateTime.TryParse(dto.Date, out var d)) date = d;
            return new ExtractedExpense(
                dto.AmountUsd is null ? null : (decimal?)dto.AmountUsd,
                NormalizeString(dto.Vendor),
                NormalizeString(dto.Description),
                date,
                NormalizeString(dto.Category),
                new(), null, "llm", null);
        }
        catch
        {
            return null;
        }
    }

    private static string? NormalizeString(string? s) =>
        string.IsNullOrWhiteSpace(s) || s.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : s.Trim();
}
