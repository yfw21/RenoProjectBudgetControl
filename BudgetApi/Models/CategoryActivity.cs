using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class CategoryActivity
{
    public int Id { get; set; }
    public int ProjectId { get; set; }
    [Required] public string CategoryValue { get; set; } = "";
    [Required] public string Name { get; set; } = "";
    public int WorkHours { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int? BudgetLineId { get; set; }
}
