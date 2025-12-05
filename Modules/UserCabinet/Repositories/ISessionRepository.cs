using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Modules.UserCabinet.Repositories;

public interface ISessionRepository
{
    Task<Session?> GetByAccessTokenAsync(string accessToken);
    Task<Session?> GetByRefreshTokenAsync(string refreshToken);
    Task<Session?> GetByIdAsync(Guid sessionId);
    Task<List<Session>> GetActiveSessionsByCounterpartyIdAsync(Guid counterpartyId);
    Task<List<Session>> GetActiveSessionsByUserIdAsync(Guid userId);
    Task<Session> CreateAsync(Session session);
    Task<Session> UpdateAsync(Session session);
    Task DeleteAsync(Guid sessionId);
    Task<bool> DeactivateSessionsByIdsAsync(List<Guid> sessionIds);
}

