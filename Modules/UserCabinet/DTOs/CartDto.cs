namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class CartDto
{
    public Guid Id { get; set; }
    public List<CartItemDto> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
}

public class CartItemDto
{
    public Guid Id { get; set; }
    public Guid NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal PriceWithDiscount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class AddCartItemDto
{
    public Guid NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

