using PDFtoImage;
using SkiaSharp;

namespace BudgetApi.Services.Receipts;

public static class PdfImageRenderer
{
    public static byte[]? RenderFirstPageToPng(byte[] pdfBytes, int dpi = 200)
    {
        try
        {
            var bitmaps = Conversion.ToImages(pdfBytes, options: new(Dpi: dpi)).ToList();
            if (bitmaps.Count == 0) return null;
            using var bmp = bitmaps[0];
            using var image = SKImage.FromBitmap(bmp);
            using var data = image.Encode(SKEncodedImageFormat.Png, 90);
            foreach (var b in bitmaps.Skip(1)) b.Dispose();
            return data.ToArray();
        }
        catch
        {
            return null;
        }
    }
}
