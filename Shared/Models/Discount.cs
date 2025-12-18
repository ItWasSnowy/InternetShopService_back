namespace InternetShopService_back.Shared.Models;

public class Discount
{
    public Guid Id { get; set; }
    public Guid CounterpartyId { get; set; }
    public int? NomenclatureGroupId { get; set; } // null если скидка на конкретную позицию
    public int? NomenclatureId { get; set; } // null если скидка на группу
    public decimal DiscountPercent { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual Counterparty Counterparty { get; set; } = null!;
}

