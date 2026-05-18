using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class Category
{
    public int Id { get; set; }
    [Required] public string Value { get; set; } = "";
    public string? Description { get; set; }
}
