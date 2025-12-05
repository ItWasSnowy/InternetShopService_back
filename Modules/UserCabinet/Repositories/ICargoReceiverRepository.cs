using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public interface ICargoReceiverRepository
{
    Task<CargoReceiver?> GetByIdAsync(Guid id);
    Task<List<CargoReceiver>> GetByUserIdAsync(Guid userId);
    Task<CargoReceiver?> GetDefaultByUserIdAsync(Guid userId);
    Task<CargoReceiver> CreateAsync(CargoReceiver receiver);
    Task<CargoReceiver> UpdateAsync(CargoReceiver receiver);
    Task<bool> DeleteAsync(Guid id);
    Task SetDefaultAsync(Guid userId, Guid receiverId);
}

