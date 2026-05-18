namespace BudgetApi.Models;

public class Note
{
    public int Id { get; set; }
    public int? ProjectId { get; set; }
    public string Text { get; set; } = "";
    public string Source { get; set; } = "user";
    public int? FileBlobId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? Embedding { get; set; }
}
