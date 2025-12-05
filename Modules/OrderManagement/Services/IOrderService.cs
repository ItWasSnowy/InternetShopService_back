using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderDto dto);
    Task<OrderDto> CreateOrderFromCartAsync(Guid userId, CreateOrderFromCartDto dto, List<CreateOrderItemDto> items);
    Task<OrderDto> GetOrderAsync(Guid orderId);
    Task<List<OrderDto>> GetOrdersByUserAsync(Guid userId);
    Task<OrderDto> UpdateOrderStatusAsync(Guid orderId, OrderStatus status);
}

