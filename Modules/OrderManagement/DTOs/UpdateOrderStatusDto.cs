using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.DTOs;

public class UpdateOrderStatusDto
{
    public OrderStatus Status { get; set; }
}

