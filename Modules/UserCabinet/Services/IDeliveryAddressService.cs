using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public interface IDeliveryAddressService
{
    Task<List<DeliveryAddressDto>> GetAddressesAsync(Guid userId);
    Task<DeliveryAddressDto?> GetAddressAsync(Guid userId, Guid addressId);
    Task<DeliveryAddressDto?> GetDefaultAddressAsync(Guid userId);
    Task<DeliveryAddressDto> CreateAddressAsync(Guid userId, CreateDeliveryAddressDto dto);
    Task<DeliveryAddressDto> UpdateAddressAsync(Guid userId, Guid addressId, UpdateDeliveryAddressDto dto);
    Task<bool> DeleteAddressAsync(Guid userId, Guid addressId);
    Task<DeliveryAddressDto> SetDefaultAddressAsync(Guid userId, Guid addressId);
}

