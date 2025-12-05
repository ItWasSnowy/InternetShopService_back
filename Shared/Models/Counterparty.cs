using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Shared.Models;

public class Counterparty
{
    public Guid Id { get; set; }
    public int? FimBizContractorId { get; set; } // ID контрагента в FimBiz (int32)
    public int? FimBizCompanyId { get; set; } // ID компании в FimBiz
    public int? FimBizOrganizationId { get; set; } // ID организации в FimBiz
    public int? LastSyncVersion { get; set; } // Последняя версия синхронизации
    public string Name { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public CounterpartyType Type { get; set; }
    public string? Email { get; set; }
    public string? Inn { get; set; }
    public string? Kpp { get; set; }
    public string? LegalAddress { get; set; }
    public string? EdoIdentifier { get; set; } // Идентификатор ЭДО для B2B
    public bool HasPostPayment { get; set; } // Постоплата
    public bool IsCreateCabinet { get; set; } // Флаг создания личного кабинета в интернет-магазине
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual UserAccount? UserAccount { get; set; }
    public virtual ICollection<Discount> Discounts { get; set; } = new List<Discount>();
}

