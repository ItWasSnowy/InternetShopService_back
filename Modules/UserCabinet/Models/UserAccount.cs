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
    
    /// <summary>
    /// Пин-код, отправленный в последнем звонке (последние 4 цифры)
    /// </summary>
    public string? PhoneCallDigits { get; set; }
    
    /// <summary>
    /// Время отправки последнего звонка (в UTC)
    /// </summary>
    public DateTime? PhoneCallDateTime { get; set; }
    
    /// <summary>
    /// Новый номер телефона, который пользователь хочет установить
    /// </summary>
    public string? NewPhoneNumber { get; set; }
    
    /// <summary>
    /// Количество неудачных попыток входа
    /// </summary>
    public int AccessFailedCount { get; set; }
    
    /// <summary>
    /// Время первой неудачной попытки входа
    /// </summary>
    public DateTime? FirstFailedLoginAttempt { get; set; }
    
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

