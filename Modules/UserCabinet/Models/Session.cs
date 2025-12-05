namespace InternetShopService_back.Modules.UserCabinet.Models;

public class Session
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public bool IsActive { get; set; } = true;
    
    // Информация об устройстве
    public string? DeviceInfo { get; set; }  // Информация об устройстве (например: "Chrome on Windows")
    public string? UserAgent { get; set; }    // User-Agent браузера
    public string? IpAddress { get; set; }    // IP адрес входа
    public string? DeviceName { get; set; }   // Название устройства (опционально)

    // Navigation properties
    public virtual UserAccount UserAccount { get; set; } = null!;
}

