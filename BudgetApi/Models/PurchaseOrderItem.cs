namespace BudgetApi.Models;

public class PurchaseOrderItem
{
    public int Id { get; set; }
    public int PurchaseOrderId { get; set; }
    public int MaterialId { get; set; }
    public decimal Quantity { get; set; }
    public decimal UnitPriceUsd { get; set; }
    public decimal? ReceivedQuantity { get; set; }
}
