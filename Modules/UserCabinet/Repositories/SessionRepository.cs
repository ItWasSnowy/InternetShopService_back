using InternetShopService_back.Data;
using InternetShopService_back.Modules.UserCabinet.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public class SessionRepository : ISessionRepository
{
    private readonly ApplicationDbContext _context;

    public SessionRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Session?> GetByAccessTokenAsync(string accessToken)
    {
        return await _context.Sessions
            .Include(s => s.UserAccount)
            .FirstOrDefaultAsync(s => s.AccessToken == accessToken && s.IsActive);
    }

    public async Task<Session?> GetByRefreshTokenAsync(string refreshToken)
    {
        return await _context.Sessions
            .Include(s => s.UserAccount)
            .FirstOrDefaultAsync(s => s.RefreshToken == refreshToken && s.IsActive);
    }

    public async Task<Session?> GetByIdAsync(Guid sessionId)
    {
        return await _context.Sessions
            .Include(s => s.UserAccount)
            .FirstOrDefaultAsync(s => s.Id == sessionId);
    }

    public async Task<List<Session>> GetActiveSessionsByCounterpartyIdAsync(Guid counterpartyId)
    {
        return await _context.Sessions
            .Include(s => s.UserAccount)
            .Where(s => s.UserAccount.CounterpartyId == counterpartyId 
                && s.IsActive 
                && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Session>> GetActiveSessionsByUserIdAsync(Guid userId)
    {
        return await _context.Sessions
            .Where(s => s.UserAccountId == userId 
                && s.IsActive 
                && s.ExpiresAt > DateTime.UtcNow)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();
    }

    public async Task<Session> CreateAsync(Session session)
    {
        session.CreatedAt = DateTime.UtcNow;
        
        _context.Sessions.Add(session);
        await _context.SaveChangesAsync();
        
        return session;
    }

    public async Task<Session> UpdateAsync(Session session)
    {
        _context.Sessions.Update(session);
        await _context.SaveChangesAsync();
        
        return session;
    }

    public async Task DeleteAsync(Guid sessionId)
    {
        var session = await _context.Sessions.FindAsync(sessionId);
        if (session != null)
        {
            _context.Sessions.Remove(session);
            await _context.SaveChangesAsync();
        }
    }

    public async Task<bool> DeactivateSessionsByIdsAsync(List<Guid> sessionIds)
    {
        var sessions = await _context.Sessions
            .Where(s => sessionIds.Contains(s.Id))
            .ToListAsync();

        foreach (var session in sessions)
        {
            session.IsActive = false;
        }

        if (sessions.Any())
        {
            await _context.SaveChangesAsync();
            return true;
        }

        return false;
    }
}

