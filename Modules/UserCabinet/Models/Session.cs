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

    // Navigation properties
    public virtual UserAccount UserAccount { get; set; } = null!;
}

