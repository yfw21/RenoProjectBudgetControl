using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class Crew
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    public int Headcount { get; set; } = 1;
    public string? Skills { get; set; }
    public decimal DayRateUsd { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
