using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Modules.OrderManagement.Models;

public class Order
{
    public Guid Id { get; set; }
    public Guid UserAccountId { get; set; }
    public Guid CounterpartyId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public DeliveryType DeliveryType { get; set; }
    public Guid? DeliveryAddressId { get; set; }
    public Guid? CargoReceiverId { get; set; }
    public Guid? CarrierId { get; set; } // ID транспортной компании
    public string? TrackingNumber { get; set; } // Трек-номер для отслеживания
    public decimal TotalAmount { get; set; }
    public bool IsPriority { get; set; } // Приоритетный заказ
    public bool IsLongAssembling { get; set; } // Флаг долгой сборки
    public Guid? InvoiceId { get; set; }
    public Guid? UpdDocumentId { get; set; }
    public Guid? AssemblerId { get; set; } // ID сборщика
    public Guid? DriverId { get; set; } // ID водителя/экспедитора
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public DateTime? AssembledAt { get; set; }
    public DateTime? ShippedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }

    // Navigation properties
    public virtual UserAccount UserAccount { get; set; } = null!;
    public virtual Counterparty Counterparty { get; set; } = null!;
    public virtual DeliveryAddress? DeliveryAddress { get; set; }
    public virtual CargoReceiver? CargoReceiver { get; set; }
    public virtual Invoice? Invoice { get; set; }
    public virtual UpdDocument? UpdDocument { get; set; }
    public virtual ICollection<OrderItem> Items { get; set; } = new List<OrderItem>();
    public virtual ICollection<OrderStatusHistory> StatusHistory { get; set; } = new List<OrderStatusHistory>();
    public virtual ICollection<OrderAttachment> Attachments { get; set; } = new List<OrderAttachment>();
}

