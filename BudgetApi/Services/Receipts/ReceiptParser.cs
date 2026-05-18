using System.Globalization;
using System.Text.RegularExpressions;

namespace BudgetApi.Services.Receipts;

public record ParsedReceipt(decimal? Amount, string? Vendor, DateTime? Date);

public static class ReceiptParser
{
    private static readonly Regex TotalAmountRegex = new(
        @"(?:grand\s*total|total\s*due|amount\s*due|balance\s*due|total)\s*[:.]?\s*\$?\s*([0-9]{1,3}(?:,[0-9]{3})*\.[0-9]{2}|[0-9]+\.[0-9]{2})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex AnyAmountRegex = new(
        @"\$\s*([0-9]{1,3}(?:,[0-9]{3})*\.[0-9]{2}|[0-9]+\.[0-9]{2})",
        RegexOptions.Compiled);

    private static readonly Regex DateRegex = new(
        @"\b(\d{4}-\d{2}-\d{2}|\d{1,2}/\d{1,2}/\d{2,4}|\d{1,2}-\d{1,2}-\d{2,4}|(?:Jan|Feb|Mar|Apr|May|Jun|Jul|Aug|Sep|Oct|Nov|Dec)[a-z]*\s+\d{1,2},?\s+\d{4})\b",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static ParsedReceipt Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new(null, null, null);

        decimal? amount = null;
        var totalMatch = TotalAmountRegex.Match(text);
        if (totalMatch.Success && TryParseAmount(totalMatch.Groups[1].Value, out var t)) amount = t;
        if (amount is null)
        {
            var amounts = AnyAmountRegex.Matches(text)
                .Select(m => TryParseAmount(m.Groups[1].Value, out var v) ? v : 0m)
                .Where(v => v > 0).ToList();
            if (amounts.Count > 0) amount = amounts.Max();
        }

        DateTime? date = null;
        foreach (Match m in DateRegex.Matches(text))
        {
            if (DateTime.TryParse(m.Value, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var dt) && dt.Year > 2000 && dt.Year < 2100)
            {
                date = dt.Date;
                break;
            }
        }

        var vendor = text.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length is > 2 and < 60
                && !char.IsDigit(l[0])
                && !l.Contains('$')
                && !l.StartsWith("http", StringComparison.OrdinalIgnoreCase));

        return new ParsedReceipt(amount, vendor, date);
    }

    private static bool TryParseAmount(string s, out decimal v) =>
        decimal.TryParse(s.Replace(",", ""), NumberStyles.Number, CultureInfo.InvariantCulture, out v);
}
