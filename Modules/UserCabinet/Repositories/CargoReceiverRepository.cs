using InternetShopService_back.Data;
using InternetShopService_back.Modules.UserCabinet.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public class CargoReceiverRepository : ICargoReceiverRepository
{
    private readonly ApplicationDbContext _context;

    public CargoReceiverRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<CargoReceiver?> GetByIdAsync(Guid id)
    {
        return await _context.CargoReceivers
            .FirstOrDefaultAsync(r => r.Id == id);
    }

    public async Task<List<CargoReceiver>> GetByUserIdAsync(Guid userId)
    {
        return await _context.CargoReceivers
            .Where(r => r.UserAccountId == userId)
            .OrderByDescending(r => r.IsDefault)
            .ThenByDescending(r => r.CreatedAt)
            .ToListAsync();
    }

    public async Task<CargoReceiver?> GetDefaultByUserIdAsync(Guid userId)
    {
        return await _context.CargoReceivers
            .FirstOrDefaultAsync(r => r.UserAccountId == userId && r.IsDefault);
    }

    public async Task<CargoReceiver> CreateAsync(CargoReceiver receiver)
    {
        receiver.CreatedAt = DateTime.UtcNow;
        receiver.UpdatedAt = DateTime.UtcNow;

        // Если это первый грузополучатель или установлен как дефолтный, снимаем флаг с других
        if (receiver.IsDefault)
        {
            await SetDefaultAsync(receiver.UserAccountId, receiver.Id);
        }

        _context.CargoReceivers.Add(receiver);
        await _context.SaveChangesAsync();

        return receiver;
    }

    public async Task<CargoReceiver> UpdateAsync(CargoReceiver receiver)
    {
        receiver.UpdatedAt = DateTime.UtcNow;

        // Если установлен как дефолтный, снимаем флаг с других
        if (receiver.IsDefault)
        {
            await SetDefaultAsync(receiver.UserAccountId, receiver.Id);
        }

        _context.CargoReceivers.Update(receiver);
        await _context.SaveChangesAsync();

        return receiver;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var receiver = await _context.CargoReceivers.FindAsync(id);
        if (receiver == null)
            return false;

        _context.CargoReceivers.Remove(receiver);
        await _context.SaveChangesAsync();
        return true;
    }

    public async Task SetDefaultAsync(Guid userId, Guid receiverId)
    {
        // Снимаем флаг IsDefault со всех грузополучателей пользователя
        var receivers = await _context.CargoReceivers
            .Where(r => r.UserAccountId == userId)
            .ToListAsync();

        foreach (var rec in receivers)
        {
            rec.IsDefault = rec.Id == receiverId;
        }

        await _context.SaveChangesAsync();
    }
}

