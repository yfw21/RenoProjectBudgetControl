using System.Text;
using UglyToad.PdfPig;

namespace BudgetApi.Services.Receipts;

public static class PdfTextExtractor
{
    public static string Extract(Stream stream)
    {
        using var pdf = PdfDocument.Open(stream);
        var sb = new StringBuilder();
        foreach (var page in pdf.GetPages())
        {
            sb.AppendLine(page.Text);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
