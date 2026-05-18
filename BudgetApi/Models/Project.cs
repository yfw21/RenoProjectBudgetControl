using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class Project
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    public string? Address { get; set; }
    public string? Client { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public string Status { get; set; } = "active";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
