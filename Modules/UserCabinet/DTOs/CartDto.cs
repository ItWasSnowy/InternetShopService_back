using System.ComponentModel.DataAnnotations;

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
    [Required(ErrorMessage = "NomenclatureId обязателен")]
    public Guid NomenclatureId { get; set; }
    
    [Required(ErrorMessage = "Название номенклатуры обязательно")]
    [StringLength(500, ErrorMessage = "Название не должно превышать 500 символов")]
    public string NomenclatureName { get; set; } = string.Empty;
    
    [Range(1, int.MaxValue, ErrorMessage = "Количество должно быть больше 0")]
    public int Quantity { get; set; }
    
    [Range(0, double.MaxValue, ErrorMessage = "Цена не может быть отрицательной")]
    public decimal Price { get; set; }
}

