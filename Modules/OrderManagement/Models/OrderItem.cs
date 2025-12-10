namespace InternetShopService_back.Modules.OrderManagement.Models;

public class OrderItem
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public Guid NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TotalAmount { get; set; }
    public string? UrlPhotosJson { get; set; } // JSON массив URL фотографий
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
}

