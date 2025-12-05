using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public class OrderStatusService : IOrderStatusService
{
    public Task<bool> CanTransitionToStatusAsync(OrderStatus currentStatus, OrderStatus newStatus)
    {
        throw new NotImplementedException();
    }

    public Task NotifyStatusChangeAsync(Guid orderId, OrderStatus newStatus)
    {
        throw new NotImplementedException();
    }
}

