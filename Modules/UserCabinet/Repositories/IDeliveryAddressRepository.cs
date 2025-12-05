using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public interface IDeliveryAddressRepository
{
    Task<DeliveryAddress?> GetByIdAsync(Guid id);
    Task<List<DeliveryAddress>> GetByUserIdAsync(Guid userId);
    Task<DeliveryAddress?> GetDefaultByUserIdAsync(Guid userId);
    Task<DeliveryAddress> CreateAsync(DeliveryAddress address);
    Task<DeliveryAddress> UpdateAsync(DeliveryAddress address);
    Task<bool> DeleteAsync(Guid id);
    Task SetDefaultAsync(Guid userId, Guid addressId);
}

