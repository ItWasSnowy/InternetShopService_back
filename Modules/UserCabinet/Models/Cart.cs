namespace InternetShopService_back.Modules.UserCabinet.Models;

public class Cart
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual UserAccount UserAccount { get; set; } = null!;
    public virtual ICollection<CartItem> Items { get; set; } = new List<CartItem>();
}

