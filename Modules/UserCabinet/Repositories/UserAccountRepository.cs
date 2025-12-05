using InternetShopService_back.Data;
using InternetShopService_back.Modules.UserCabinet.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public class UserAccountRepository : IUserAccountRepository
{
    private readonly ApplicationDbContext _context;

    public UserAccountRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<UserAccount?> GetByPhoneNumberAsync(string phoneNumber)
    {
        return await _context.UserAccounts
            .Include(u => u.Counterparty)
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber);
    }

    public async Task<UserAccount?> GetByIdAsync(Guid id)
    {
        return await _context.UserAccounts
            .Include(u => u.Counterparty)
            .FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<UserAccount> CreateAsync(UserAccount userAccount)
    {
        userAccount.CreatedAt = DateTime.UtcNow;
        userAccount.UpdatedAt = DateTime.UtcNow;
        
        _context.UserAccounts.Add(userAccount);
        await _context.SaveChangesAsync();
        
        return userAccount;
    }

    public async Task<UserAccount> UpdateAsync(UserAccount userAccount)
    {
        userAccount.UpdatedAt = DateTime.UtcNow;
        
        _context.UserAccounts.Update(userAccount);
        await _context.SaveChangesAsync();
        
        return userAccount;
    }

    public async Task<List<Session>> GetActiveSessionsAsync(Guid userId)
    {
        return await _context.Sessions
            .Where(s => s.UserAccountId == userId && s.IsActive && s.ExpiresAt > DateTime.UtcNow)
            .ToListAsync();
    }

    public async Task DeactivateSessionsAsync(Guid userId, Guid? excludeSessionId = null)
    {
        var sessions = await _context.Sessions
            .Where(s => s.UserAccountId == userId 
                && s.IsActive 
                && s.ExpiresAt > DateTime.UtcNow
                && (excludeSessionId == null || s.Id != excludeSessionId.Value))
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.IsActive = false;
        }

        await _context.SaveChangesAsync();
    }

    public async Task<bool> ExistsByPhoneNumberAsync(string phoneNumber)
    {
        return await _context.UserAccounts
            .AnyAsync(u => u.PhoneNumber == phoneNumber);
    }
}

