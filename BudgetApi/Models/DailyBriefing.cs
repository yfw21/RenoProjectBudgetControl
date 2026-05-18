namespace BudgetApi.Models;

public class DailyBriefing
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public DateTime Date { get; set; }
    public string Text { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
