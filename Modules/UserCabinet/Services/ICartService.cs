using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public interface ICartService
{
    Task<CartDto> GetCartAsync(Guid userId);
    Task<CartDto> AddItemAsync(Guid userId, AddCartItemDto item);
    Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, int quantity);
    Task<bool> RemoveItemAsync(Guid userId, Guid itemId);
    Task<bool> ClearCartAsync(Guid userId);
}

