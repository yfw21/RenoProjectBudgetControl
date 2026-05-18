using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class Equipment
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    public string? Type { get; set; }
    public decimal DayRateUsd { get; set; }
    public bool Rented { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
