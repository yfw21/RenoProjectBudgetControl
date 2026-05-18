using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class FileBlob
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public int UploadedByUserId { get; set; }
    [Required] public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public string StoragePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Kind { get; set; } = "other";
    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
