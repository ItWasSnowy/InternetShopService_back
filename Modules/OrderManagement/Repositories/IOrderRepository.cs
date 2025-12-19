using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.Repositories;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(Guid id);
    Task<Order?> GetByFimBizOrderIdAsync(int fimBizOrderId);
    Task<Order?> GetByOrderNumberAsync(string orderNumber);
    Task<List<Order>> GetByUserIdAsync(Guid userId);
    Task<(List<Order> Orders, int TotalCount)> GetByUserIdPagedAsync(Guid userId, int page, int pageSize);
    Task<Order> CreateAsync(Order order);
    Task<Order> UpdateAsync(Order order);
    Task<bool> DeleteAsync(Guid id);
    Task<string> GenerateOrderNumberAsync();
    
    // Метод для получения неотправленных заказов
    Task<List<Order>> GetUnsyncedOrdersAsync(int limit = 100);
}

