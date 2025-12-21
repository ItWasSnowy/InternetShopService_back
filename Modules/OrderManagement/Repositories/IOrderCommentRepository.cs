using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Modules.OrderManagement.Repositories;

public interface IOrderCommentRepository
{
    Task<OrderComment?> GetByIdAsync(Guid id);
    Task<OrderComment?> GetByExternalCommentIdAsync(string externalCommentId);
    Task<List<OrderComment>> GetByOrderIdAsync(Guid orderId);
    Task<OrderComment> CreateAsync(OrderComment comment);
    Task<OrderComment> UpdateAsync(OrderComment comment);
    Task<bool> DeleteAsync(Guid id);
}





