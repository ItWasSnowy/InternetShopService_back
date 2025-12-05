using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class CartService : ICartService
{
    public Task<CartDto> GetCartAsync(Guid userId)
    {
        throw new NotImplementedException();
    }

    public Task<CartDto> AddItemAsync(Guid userId, AddCartItemDto item)
    {
        throw new NotImplementedException();
    }

    public Task<CartDto> UpdateItemAsync(Guid userId, Guid itemId, int quantity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> RemoveItemAsync(Guid userId, Guid itemId)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ClearCartAsync(Guid userId)
    {
        throw new NotImplementedException();
    }
}

