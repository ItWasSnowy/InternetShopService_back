using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id);
    Task<List<Order>> GetByUserIdAsync(Guid userId);
    Task<Order> CreateAsync(Order order);
    Task<Order> UpdateAsync(Order order);
    Task<string> GenerateOrderNumberAsync();
}

