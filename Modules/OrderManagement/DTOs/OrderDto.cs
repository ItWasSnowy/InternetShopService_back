using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.DTOs;

public class OrderDto
{
    public Guid Id { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public OrderStatus Status { get; set; }
    public string StatusName { get; set; } = string.Empty;
    public DeliveryType DeliveryType { get; set; }
    public string? TrackingNumber { get; set; }
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public List<OrderItemDto> Items { get; set; } = new();
    public DeliveryAddressDto? DeliveryAddress { get; set; }
    public CargoReceiverDto? CargoReceiver { get; set; }
    public List<OrderAttachmentDto> Attachments { get; set; } = new();
}

public class OrderItemDto
{
    public Guid Id { get; set; }
    public Guid NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TotalAmount { get; set; }
}

public class DeliveryAddressDto
{
    public Guid Id { get; set; }
    public string Address { get; set; } = string.Empty;
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
}

public class CargoReceiverDto
{
    public Guid Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string PassportSeries { get; set; } = string.Empty;
    public string PassportNumber { get; set; } = string.Empty;
}

public class OrderAttachmentDto
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public bool IsVisibleToCustomer { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateOrderDto
{
    public DeliveryType DeliveryType { get; set; }
    public Guid? DeliveryAddressId { get; set; }
    public Guid? CargoReceiverId { get; set; }
    public Guid? CarrierId { get; set; }
    public List<CreateOrderItemDto> Items { get; set; } = new();
}

public class CreateOrderItemDto
{
    public Guid NomenclatureId { get; set; }
    public string NomenclatureName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

