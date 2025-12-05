namespace InternetShopService_back.Modules.UserCabinet.Models;

public class DeliveryAddress
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Apartment { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual UserAccount UserAccount { get; set; } = null!;
}

