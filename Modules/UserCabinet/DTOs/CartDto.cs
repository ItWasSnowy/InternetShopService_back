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
    public int NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public string? UnitType { get; set; }
    public string? Sku { get; set; }
    public List<string> UrlPhotos { get; set; } = new();
    public decimal DiscountPercent { get; set; }
    public decimal PriceWithDiscount { get; set; }
    public decimal TotalAmount { get; set; }
}

public class AddCartItemDto
{
    [Required(ErrorMessage = "NomenclatureId обязателен")]
    public int NomenclatureId { get; set; }
    
    [Required(ErrorMessage = "Название номенклатуры обязательно")]
    [StringLength(500, ErrorMessage = "Название не должно превышать 500 символов")]
    public string NomenclatureName { get; set; } = string.Empty;
    
    [Range(1, int.MaxValue, ErrorMessage = "Количество должно быть больше 0")]
    public int Quantity { get; set; }
    
    [Range(0, double.MaxValue, ErrorMessage = "Цена не может быть отрицательной")]
    public decimal Price { get; set; }
    
    [StringLength(50, ErrorMessage = "Единица измерения не должна превышать 50 символов")]
    public string? UnitType { get; set; }
    
    [StringLength(100, ErrorMessage = "Артикул не должен превышать 100 символов")]
    public string? Sku { get; set; }
    
    public List<string>? UrlPhotos { get; set; }
}

