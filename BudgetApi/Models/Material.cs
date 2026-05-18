using System.ComponentModel.DataAnnotations;

namespace BudgetApi.Models;

public class Material
{
    public int Id { get; set; }
    [Required] public string Name { get; set; } = "";
    public string Unit { get; set; } = "ea";
    public decimal? DefaultUnitPriceUsd { get; set; }
}
