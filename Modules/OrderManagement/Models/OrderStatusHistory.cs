namespace InternetShopService_back.Modules.OrderManagement.Models;

public class OrderStatusHistory
{
    public Guid Id { get; set; }
    public Guid OrderId { get; set; }
    public OrderStatus Status { get; set; }
    public string? Comment { get; set; }
    public DateTime ChangedAt { get; set; }
    public Guid? ChangedBy { get; set; } // ID сотрудника, изменившего статус

    // Navigation properties
    public virtual Order Order { get; set; } = null!;
}

