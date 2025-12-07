namespace InternetShopService_back.Modules.UserCabinet.Models;

public class CartItem
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public Guid NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? UnitType { get; set; } // Единица измерения (шт, кг, л и т.д.)
    public string? Sku { get; set; } // Артикул
    public string? UrlPhotosJson { get; set; } // JSON массив URL фотографий
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual Cart Cart { get; set; } = null!;
}

