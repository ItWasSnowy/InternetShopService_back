using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class SessionService : ISessionService
{
    private readonly ISessionRepository _sessionRepository;
    private readonly ILogger<SessionService> _logger;

    public SessionService(
        ISessionRepository sessionRepository,
        ILogger<SessionService> logger)
    {
        _sessionRepository = sessionRepository;
        _logger = logger;
    }

    public async Task<List<SessionDto>> GetActiveSessionsAsync(Guid userId)
    {
        var sessions = await _sessionRepository.GetActiveSessionsByUserIdAsync(userId);
        
        return sessions.Select(s => new SessionDto
        {
            Id = s.Id,
            CreatedAt = s.CreatedAt,
            ExpiresAt = s.ExpiresAt,
            IsActive = s.IsActive,
            DeviceInfo = s.DeviceInfo,
            UserAgent = s.UserAgent,
            IpAddress = s.IpAddress,
            DeviceName = s.DeviceName,
            IsCurrentSession = false // Будет установлено в контроллере
        }).ToList();
    }

    public async Task<bool> DeactivateSessionAsync(Guid sessionId, Guid userId)
    {
        var session = await _sessionRepository.GetByIdAsync(sessionId);
        
        if (session == null || session.UserAccountId != userId)
        {
            return false;
        }

        session.IsActive = false;
        await _sessionRepository.UpdateAsync(session);
        
        _logger.LogInformation("Сессия {SessionId} деактивирована пользователем {UserId}", sessionId, userId);
        
        return true;
    }

    public async Task<bool> DeactivateSessionsAsync(List<Guid> sessionIds, Guid userId)
    {
        // Проверяем, что все сессии принадлежат пользователю
        var sessions = new List<Session>();
        foreach (var sessionId in sessionIds)
        {
            var session = await _sessionRepository.GetByIdAsync(sessionId);
            if (session != null && session.UserAccountId == userId)
            {
                sessions.Add(session);
            }
        }

        if (!sessions.Any())
        {
            return false;
        }

        var result = await _sessionRepository.DeactivateSessionsByIdsAsync(sessionIds);
        
        _logger.LogInformation("Деактивировано {Count} сессий пользователем {UserId}", sessions.Count, userId);
        
        return result;
    }
}
