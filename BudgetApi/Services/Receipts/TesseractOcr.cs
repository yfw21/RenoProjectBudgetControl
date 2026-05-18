using System.Diagnostics;

namespace BudgetApi.Services.Receipts;

public class TesseractOcr
{
    private readonly ILogger<TesseractOcr> _log;
    public TesseractOcr(ILogger<TesseractOcr> log) { _log = log; }

    public bool IsAvailable()
    {
        try
        {
            var psi = new ProcessStartInfo("tesseract", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(2000);
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> OcrAsync(byte[] imageBytes, CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo("tesseract", "- - -l eng")
            {
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start tesseract");

            await proc.StandardInput.BaseStream.WriteAsync(imageBytes, ct);
            proc.StandardInput.Close();

            var outputTask = proc.StandardOutput.ReadToEndAsync(ct);
            var errorTask = proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            var output = await outputTask;
            if (proc.ExitCode != 0)
            {
                var err = await errorTask;
                _log.LogWarning("Tesseract exit code {Code}: {Err}", proc.ExitCode, err.Trim());
                return null;
            }
            return output;
        }
        catch (Exception e)
        {
            _log.LogWarning("Tesseract OCR failed: {Msg}", e.Message);
            return null;
        }
    }
}
