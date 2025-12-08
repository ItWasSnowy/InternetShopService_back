using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class CreateOrderFromCartDto
{
    public DeliveryType DeliveryType { get; set; }
    public Guid? DeliveryAddressId { get; set; }
    public Guid? CargoReceiverId { get; set; }
    public string? Carrier { get; set; } // Название транспортной компании
}

