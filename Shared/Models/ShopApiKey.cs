namespace InternetShopService_back.Shared.Models;

public class ShopApiKey
{
    public Guid Id { get; set; }
    public Guid ShopId { get; set; }
    public string ApiKey { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public virtual Shop Shop { get; set; } = null!;
}
