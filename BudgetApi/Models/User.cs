using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class User
{
    public int Id { get; set; }
    [Required] public string Username { get; set; } = "";
    [Required] public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = "user";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
