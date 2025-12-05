using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public interface ICartRepository
{
    Task<Cart?> GetByUserIdAsync(Guid userId);
    Task<Cart> CreateAsync(Cart cart);
    Task<Cart> UpdateAsync(Cart cart);
    Task<bool> DeleteAsync(Guid cartId);
    Task<CartItem?> GetCartItemByIdAsync(Guid itemId);
    Task<CartItem> AddCartItemAsync(CartItem item);
    Task<CartItem> UpdateCartItemAsync(CartItem item);
    Task<bool> RemoveCartItemAsync(Guid itemId);
    Task ClearCartItemsAsync(Guid cartId);
}

