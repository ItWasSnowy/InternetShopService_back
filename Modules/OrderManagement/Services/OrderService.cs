using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public class OrderService : IOrderService
{
    public Task<OrderDto> CreateOrderAsync(CreateOrderDto dto)
    {
        throw new NotImplementedException();
    }

    public Task<OrderDto> GetOrderAsync(Guid orderId)
    {
        throw new NotImplementedException();
    }

    public Task<List<OrderDto>> GetOrdersByUserAsync(Guid userId)
    {
        throw new NotImplementedException();
    }

    public Task<OrderDto> UpdateOrderStatusAsync(Guid orderId, OrderStatus status)
    {
        throw new NotImplementedException();
    }
}

