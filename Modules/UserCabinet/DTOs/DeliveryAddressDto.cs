namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class DeliveryAddressDto
{
    public Guid Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Apartment { get; set; }
    public bool IsDefault { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateDeliveryAddressDto
{
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Apartment { get; set; }
    public bool IsDefault { get; set; }
}

public class UpdateDeliveryAddressDto
{
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Apartment { get; set; }
    public bool IsDefault { get; set; }
}

