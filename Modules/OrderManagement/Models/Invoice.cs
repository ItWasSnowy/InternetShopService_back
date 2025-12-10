using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Modules.OrderManagement.Models;

public class Invoice
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public string? PdfUrl { get; set; } // Относительный URL для скачивания PDF счета (например, "/Files/OrderFiles/123/bill.pdf")
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
}

