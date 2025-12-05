using InternetShopService_back.Data;
using InternetShopService_back.Modules.UserCabinet.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public class CartRepository : ICartRepository
{
    private readonly ApplicationDbContext _context;

    public CartRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Cart?> GetByUserIdAsync(Guid userId)
    {
        return await _context.Carts
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.UserAccountId == userId);
    }

    public async Task<Cart> CreateAsync(Cart cart)
    {
        cart.CreatedAt = DateTime.UtcNow;
        cart.UpdatedAt = DateTime.UtcNow;
        
        _context.Carts.Add(cart);
        await _context.SaveChangesAsync();
        
        return cart;
    }

    public async Task<Cart> UpdateAsync(Cart cart)
    {
        cart.UpdatedAt = DateTime.UtcNow;
        
        _context.Carts.Update(cart);
        await _context.SaveChangesAsync();
        
        return cart;
    }

    public async Task<bool> DeleteAsync(Guid cartId)
    {
        var cart = await _context.Carts.FindAsync(cartId);
        if (cart == null)
            return false;

        _context.Carts.Remove(cart);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task<CartItem?> GetCartItemByIdAsync(Guid itemId)
    {
        return await _context.CartItems
            .Include(i => i.Cart)
            .FirstOrDefaultAsync(i => i.Id == itemId);
    }

    public async Task<CartItem> AddCartItemAsync(CartItem item)
    {
        item.CreatedAt = DateTime.UtcNow;
        item.UpdatedAt = DateTime.UtcNow;
        
        _context.CartItems.Add(item);
        await _context.SaveChangesAsync();
        
        return item;
    }

    public async Task<CartItem> UpdateCartItemAsync(CartItem item)
    {
        item.UpdatedAt = DateTime.UtcNow;
        
        _context.CartItems.Update(item);
        await _context.SaveChangesAsync();
        
        return item;
    }

    public async Task<bool> RemoveCartItemAsync(Guid itemId)
    {
        var item = await _context.CartItems.FindAsync(itemId);
        if (item == null)
            return false;

        _context.CartItems.Remove(item);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task ClearCartItemsAsync(Guid cartId)
    {
        var items = await _context.CartItems
            .Where(i => i.CartId == cartId)
            .ToListAsync();

        _context.CartItems.RemoveRange(items);
        await _context.SaveChangesAsync();
    }
}

