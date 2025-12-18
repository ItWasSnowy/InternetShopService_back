using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class CounterpartyDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public CounterpartyType Type { get; set; }
    public string? Email { get; set; }
    public string? Inn { get; set; }
    public string? Kpp { get; set; }
    public string? LegalAddress { get; set; }
    public string? EdoIdentifier { get; set; }
    public bool HasPostPayment { get; set; }
}

public class DiscountDto
{
    public Guid Id { get; set; }
    public int? NomenclatureGroupId { get; set; }
    public int? NomenclatureId { get; set; }
    public decimal DiscountPercent { get; set; }
    public DateTime ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }
    public bool IsActive { get; set; }
}

