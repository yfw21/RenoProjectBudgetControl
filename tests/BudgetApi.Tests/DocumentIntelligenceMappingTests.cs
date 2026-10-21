using System.ClientModel.Primitives;
using Azure.AI.DocumentIntelligence;
using BudgetApi.Services.Receipts;

namespace BudgetApi.Tests;

// Fixtures are analyzeResult payloads in the Document Intelligence REST wire format (2024-11-30),
// deserialized by the SDK itself, so these tests cover wire-name -> SDK property mapping too.
public class DocumentIntelligenceMappingTests
{
    private static AnalyzeResult Load(string name) =>
        ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString(File.ReadAllText(Path.Combine("Fixtures", name))))!;

    [Fact]
    public void Receipt_maps_total_vendor_date_and_items()
    {
        var r = DocumentIntelligenceExtractor.MapReceipt(Load("receipt.json"));

        Assert.Equal(80.78m, r.Amount);   // the total including tax, not the subtotal
        Assert.Equal("THE HOME DEPOT", r.Vendor);
        Assert.Equal(new DateTime(2026, 10, 3), r.Date);
        Assert.Equal("2x4x8 STUD, DRYWALL SCREWS 1LB, JOINT COMPOUND 4.5G", r.Description);
        Assert.Empty(r.Missing);
        Assert.Equal(DocumentIntelligenceExtractor.Source, r.Source);
        Assert.StartsWith("THE HOME DEPOT", r.RawText);
    }

    [Fact]
    public void Receipt_reports_per_field_confidence()
    {
        var r = DocumentIntelligenceExtractor.MapReceipt(Load("receipt.json"));

        Assert.NotNull(r.Confidence);
        Assert.Equal(0.981f, r.Confidence!["amount"], 3);
        Assert.Equal(0.972f, r.Confidence["vendor"], 3);
        Assert.Equal(0.989f, r.Confidence["date"], 3);
    }

    [Fact]
    public void Receipt_with_no_documents_lists_everything_as_missing()
    {
        var empty = ModelReaderWriter.Read<AnalyzeResult>(BinaryData.FromString(
            """{"apiVersion":"2024-11-30","modelId":"prebuilt-receipt","content":"blurry","pages":[],"documents":[]}"""))!;

        var r = DocumentIntelligenceExtractor.MapReceipt(empty);

        Assert.Null(r.Amount);
        Assert.Equal(new[] { "amount", "vendor", "description" }, r.Missing);
        Assert.NotNull(r.Note);
    }

    [Fact]
    public void Invoice_maps_header_and_line_items()
    {
        var inv = DocumentIntelligenceExtractor.MapInvoice(Load("invoice.json"));

        Assert.Equal("Harbor Electric LLC", inv.Vendor);
        Assert.Equal("INV-2291", inv.InvoiceNumber);
        Assert.Equal(new DateTime(2026, 9, 28), inv.InvoiceDate);
        Assert.Equal(1682.00m, inv.Total);
        Assert.Equal(3, inv.Lines.Count);

        var wiring = inv.Lines[0];
        Assert.Equal("Rough-in wiring, kitchen", wiring.Description);
        Assert.Equal(16m, wiring.Quantity);
        Assert.Equal(85.00m, wiring.UnitPrice);
        Assert.Equal(1360.00m, wiring.Amount);

        Assert.Null(inv.Lines[2].Quantity);   // "Permit fee" has no quantity field
        Assert.Equal(inv.Total, inv.Lines.Sum(l => l.Amount));
        Assert.Null(inv.Note);
    }

    [Theory]
    [InlineData("application/pdf", "x.pdf", true)]
    [InlineData("image/jpeg", "IMG_0001.JPG", true)]
    [InlineData("", "scan.png", true)]
    [InlineData("text/plain", "notes.txt", false)]
    [InlineData("text/csv", "export.csv", false)]
    public void Supports_only_documents_and_images(string contentType, string fileName, bool expected) =>
        Assert.Equal(expected, DocumentIntelligenceExtractor.Supports(contentType, fileName));
}
