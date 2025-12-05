using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public interface ICargoReceiverService
{
    Task<List<CargoReceiverDto>> GetReceiversAsync(Guid userId);
    Task<CargoReceiverDto?> GetReceiverAsync(Guid userId, Guid receiverId);
    Task<CargoReceiverDto?> GetDefaultReceiverAsync(Guid userId);
    Task<CargoReceiverDto> CreateReceiverAsync(Guid userId, CreateCargoReceiverDto dto);
    Task<CargoReceiverDto> UpdateReceiverAsync(Guid userId, Guid receiverId, UpdateCargoReceiverDto dto);
    Task<bool> DeleteReceiverAsync(Guid userId, Guid receiverId);
    Task<CargoReceiverDto> SetDefaultReceiverAsync(Guid userId, Guid receiverId);
}

