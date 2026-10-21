using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;

namespace BudgetApi.Services.Receipts;

/// <summary>
/// Reads receipts and invoices with Azure AI Document Intelligence prebuilt models
/// (prebuilt-receipt, prebuilt-invoice). Enabled when DocumentIntelligence:Endpoint is set;
/// otherwise the local Tesseract + Ollama pipeline is used. Authenticates with
/// DocumentIntelligence:Key if present (local testing), else Managed Identity.
/// </summary>
public class DocumentIntelligenceExtractor
{
    public const string Source = "azure-document-intelligence";
    // Fields the model is less sure about than this are flagged for review in the UI.
    public const float ReviewThreshold = 0.8f;

    private readonly DocumentIntelligenceClient? _client;
    private readonly ILogger<DocumentIntelligenceExtractor> _log;

    public DocumentIntelligenceExtractor(IConfiguration cfg, ILogger<DocumentIntelligenceExtractor> log)
    {
        _log = log;
        var endpoint = cfg["DocumentIntelligence:Endpoint"];
        if (string.IsNullOrWhiteSpace(endpoint)) return;

        // Fail over to the local pipeline quickly rather than retrying for long.
        var options = new DocumentIntelligenceClientOptions();
        options.Retry.MaxRetries = 2;
        var key = cfg["DocumentIntelligence:Key"];
        _client = !string.IsNullOrWhiteSpace(key)
            ? new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key), options)
            : new DocumentIntelligenceClient(new Uri(endpoint), new DefaultAzureCredential(
                new DefaultAzureCredentialOptions { ManagedIdentityClientId = cfg["AZURE_CLIENT_ID"] }), options);
    }

    public bool IsEnabled => _client != null;

    public static bool Supports(string? contentType, string fileName)
    {
        var ct = (contentType ?? "").ToLowerInvariant();
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ct == "application/pdf" || ct.StartsWith("image/")
            || ext is ".pdf" or ".jpg" or ".jpeg" or ".png" or ".bmp" or ".tif" or ".tiff" or ".heic";
    }

    public async Task<ExtractedExpense> AnalyzeReceiptAsync(byte[] document, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var op = await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-receipt", BinaryData.FromBytes(document), ct);
        _log.LogInformation("Document Intelligence prebuilt-receipt took {Ms}ms", sw.ElapsedMilliseconds);
        return MapReceipt(op.Value);
    }

    public async Task<ExtractedInvoice> AnalyzeInvoiceAsync(byte[] document, CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var op = await _client!.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-invoice", BinaryData.FromBytes(document), ct);
        _log.LogInformation("Document Intelligence prebuilt-invoice took {Ms}ms", sw.ElapsedMilliseconds);
        return MapInvoice(op.Value);
    }

    public static ExtractedExpense MapReceipt(AnalyzeResult result)
    {
        var doc = result.Documents.FirstOrDefault();
        if (doc == null)
            return new ExtractedExpense(null, null, null, null, null, new() { "amount", "vendor", "description" },
                Trim(result.Content, 2000), Source, "No receipt was found in the document.");

        var f = doc.Fields;
        var amount = Money(Get(f, "Total"));
        var vendor = Text(Get(f, "MerchantName"));
        var date = Get(f, "TransactionDate")?.ValueDate?.Date;

        var items = Get(f, "Items")?.ValueList ?? new List<DocumentField>();
        var itemNames = items
            .Select(i => Text(Get(i.ValueDictionary, "Description")))
            .Where(s => s != null)
            .ToList();
        var description = itemNames.Count switch
        {
            0 => null,
            <= 3 => string.Join(", ", itemNames),
            _ => $"{string.Join(", ", itemNames.Take(3))} +{itemNames.Count - 3} more",
        };

        var confidence = new Dictionary<string, float>();
        AddConfidence(confidence, "amount", Get(f, "Total"));
        AddConfidence(confidence, "vendor", Get(f, "MerchantName"));
        AddConfidence(confidence, "date", Get(f, "TransactionDate"));

        var missing = new List<string>();
        if (amount is null or 0) missing.Add("amount");
        if (string.IsNullOrWhiteSpace(vendor)) missing.Add("vendor");
        if (string.IsNullOrWhiteSpace(description)) missing.Add("description");

        return new ExtractedExpense(amount, vendor, description, date, null, missing,
            Trim(result.Content, 2000), Source, null, confidence);
    }

    public static ExtractedInvoice MapInvoice(AnalyzeResult result)
    {
        var doc = result.Documents.FirstOrDefault();
        if (doc == null)
            return new ExtractedInvoice(null, null, null, null, new(), Trim(result.Content, 4000), Source,
                "No invoice was found in the document.");

        var f = doc.Fields;
        var lines = (Get(f, "Items")?.ValueList ?? new List<DocumentField>())
            .Select(i => i.ValueDictionary)
            .Select(d => new ExtractedInvoiceLine(
                Text(Get(d, "Description")) ?? Get(d, "Description")?.Content ?? "(no description)",
                Number(Get(d, "Quantity")),
                Money(Get(d, "UnitPrice")),
                Money(Get(d, "Amount")) ?? 0m))
            .Where(l => l.Amount != 0)
            .ToList();

        return new ExtractedInvoice(
            Text(Get(f, "VendorName")),
            Text(Get(f, "InvoiceId")),
            Get(f, "InvoiceDate")?.ValueDate?.Date,
            Money(Get(f, "InvoiceTotal")) ?? Money(Get(f, "SubTotal")),
            lines,
            Trim(result.Content, 4000),
            Source,
            lines.Count == 0 ? "No line items were found; enter them manually." : null);
    }

    private static DocumentField? Get(IReadOnlyDictionary<string, DocumentField>? fields, string name) =>
        fields != null && fields.TryGetValue(name, out var v) ? v : null;

    private static string? Text(DocumentField? f)
    {
        var s = f?.ValueString ?? f?.Content;
        return string.IsNullOrWhiteSpace(s) ? null : s.Replace('\n', ' ').Trim();
    }

    private static decimal? Money(DocumentField? f) =>
        f?.ValueCurrency is { } c ? Math.Round((decimal)c.Amount, 2)
        : f?.ValueDouble is { } d ? Math.Round((decimal)d, 2)
        : null;

    private static decimal? Number(DocumentField? f) =>
        f?.ValueDouble is { } d ? (decimal)d : null;

    private static void AddConfidence(Dictionary<string, float> into, string key, DocumentField? f)
    {
        if (f?.Confidence is { } c) into[key] = c;
    }

    private static string? Trim(string? s, int max) =>
        s == null ? null : s.Length > max ? s[..max] + "…" : s;
}
