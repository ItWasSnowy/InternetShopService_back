namespace InternetShopService_back.Modules.UserCabinet.Models;

public class CartItem
{
    public Guid Id { get; set; }
    public Guid CartId { get; set; }
    public Guid NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual Cart Cart { get; set; } = null!;
}

