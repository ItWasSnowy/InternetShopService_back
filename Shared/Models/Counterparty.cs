using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Shared.Models;

public class Counterparty
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public CounterpartyType Type { get; set; }
    public string? Email { get; set; }
    public string? Inn { get; set; }
    public string? Kpp { get; set; }
    public string? LegalAddress { get; set; }
    public string? EdoIdentifier { get; set; } // Идентификатор ЭДО для B2B
    public bool HasPostPayment { get; set; } // Постоплата
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual UserAccount? UserAccount { get; set; }
    public virtual ICollection<Discount> Discounts { get; set; } = new List<Discount>();
}

