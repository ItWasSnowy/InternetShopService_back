using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public interface IOrderStatusService
{
    Task<bool> CanTransitionToStatusAsync(OrderStatus currentStatus, OrderStatus newStatus);
    Task NotifyStatusChangeAsync(Guid orderId, OrderStatus newStatus);
}

