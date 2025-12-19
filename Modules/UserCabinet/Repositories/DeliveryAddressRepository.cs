using InternetShopService_back.Data;
using InternetShopService_back.Modules.UserCabinet.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public class DeliveryAddressRepository : IDeliveryAddressRepository
{
    private readonly ApplicationDbContext _context;

    public DeliveryAddressRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<DeliveryAddress?> GetByIdAsync(Guid id)
    {
        return await _context.DeliveryAddresses
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<List<DeliveryAddress>> GetByUserIdAsync(Guid userId)
    {
        return await _context.DeliveryAddresses
            .Where(a => a.UserAccountId == userId)
            .OrderByDescending(a => a.IsDefault)
            .ThenByDescending(a => a.CreatedAt)
            .ToListAsync();
    }

    public async Task<DeliveryAddress?> GetDefaultByUserIdAsync(Guid userId)
    {
        return await _context.DeliveryAddresses
            .FirstOrDefaultAsync(a => a.UserAccountId == userId && a.IsDefault);
    }

    public async Task<DeliveryAddress> CreateAsync(DeliveryAddress address)
    {
        address.CreatedAt = DateTime.UtcNow;
        address.UpdatedAt = DateTime.UtcNow;

        // Если это первый адрес или установлен как дефолтный, снимаем флаг с других
        if (address.IsDefault)
        {
            await SetDefaultAsync(address.UserAccountId, address.Id);
        }

        _context.DeliveryAddresses.Add(address);
        await _context.SaveChangesAsync();

        return address;
    }

    public async Task<DeliveryAddress> UpdateAsync(DeliveryAddress address)
    {
        address.UpdatedAt = DateTime.UtcNow;

        // Если установлен как дефолтный, снимаем флаг с других
        if (address.IsDefault)
        {
            await SetDefaultAsync(address.UserAccountId, address.Id);
        }

        _context.DeliveryAddresses.Update(address);
        await _context.SaveChangesAsync();

        return address;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var address = await _context.DeliveryAddresses.FindAsync(id);
        if (address == null)
            return false;

        // Сначала обнуляем ссылки на этот адрес в заказах
        var ordersWithAddress = await _context.Orders
            .Where(o => o.DeliveryAddressId == id)
            .ToListAsync();
        
        foreach (var order in ordersWithAddress)
        {
            order.DeliveryAddressId = null;
        }

        _context.DeliveryAddresses.Remove(address);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task SetDefaultAsync(Guid userId, Guid addressId)
    {
        // Снимаем флаг IsDefault со всех адресов пользователя
        var addresses = await _context.DeliveryAddresses
            .Where(a => a.UserAccountId == userId)
            .ToListAsync();

        foreach (var addr in addresses)
        {
            addr.IsDefault = addr.Id == addressId;
        }

        await _context.SaveChangesAsync();
    }
}

