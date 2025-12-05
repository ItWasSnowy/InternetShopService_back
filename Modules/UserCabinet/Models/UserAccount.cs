using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Modules.UserCabinet.Models;

public class UserAccount
{
    public Guid Id { get; set; }
    public Guid CounterpartyId { get; set; }
    public string PhoneNumber { get; set; } = string.Empty;
    public string? PasswordHash { get; set; } // null если пароль не установлен
    public bool IsPasswordSet { get; set; }
    public bool IsFirstLogin { get; set; } = true; // Флаг первого входа
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // Navigation properties
    public virtual Counterparty Counterparty { get; set; } = null!;
    public virtual Cart? Cart { get; set; }
    public virtual ICollection<Session> Sessions { get; set; } = new List<Session>();
    public virtual ICollection<DeliveryAddress> DeliveryAddresses { get; set; } = new List<DeliveryAddress>();
    public virtual ICollection<CargoReceiver> CargoReceivers { get; set; } = new List<CargoReceiver>();
}

