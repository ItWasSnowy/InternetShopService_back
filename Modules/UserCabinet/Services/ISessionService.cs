using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public interface ISessionService
{
    Task<List<SessionDto>> GetActiveSessionsAsync(Guid userId);
    Task<bool> DeactivateSessionAsync(Guid sessionId, Guid userId);
    Task<bool> DeactivateSessionsAsync(List<Guid> sessionIds, Guid userId);
}
