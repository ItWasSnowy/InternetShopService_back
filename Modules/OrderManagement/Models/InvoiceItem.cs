namespace InternetShopService_back.Modules.OrderManagement.Models;

public class InvoiceItem
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string Unit { get; set; } = "шт";
    public decimal Price { get; set; }
    public decimal TotalAmount { get; set; }
    public int SortOrder { get; set; }

    // Navigation properties
    public virtual Invoice Invoice { get; set; } = null!;
}

