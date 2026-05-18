namespace BudgetApi.Models;

public class StockLevel
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    public int MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
