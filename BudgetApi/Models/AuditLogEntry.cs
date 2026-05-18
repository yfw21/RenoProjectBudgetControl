namespace BudgetApi.Models;

public class AuditLogEntry
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Tool { get; set; } = "";
    public string ArgsJson { get; set; } = "";
    public string ResultJson { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
