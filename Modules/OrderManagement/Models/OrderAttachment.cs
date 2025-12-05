namespace InternetShopService_back.Modules.OrderManagement.Models;

public class OrderAttachment
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public bool IsVisibleToCustomer { get; set; } // Разрешение на отображение покупателю
    public DateTime CreatedAt { get; set; }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
}

