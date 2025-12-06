using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Modules.UserCabinet.Services;

/// <summary>
/// Сервис для работы с сессиями для FimBiz
/// </summary>
public class FimBizSessionService
{
    private readonly ApplicationDbContext _context;
    private readonly ISessionRepository _sessionRepository;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly ILogger<FimBizSessionService> _logger;

    public FimBizSessionService(
        ApplicationDbContext context,
        ISessionRepository sessionRepository,
        ICounterpartyRepository counterpartyRepository,
        ILogger<FimBizSessionService> logger)
    {
        _context = context;
        _sessionRepository = sessionRepository;
        _counterpartyRepository = counterpartyRepository;
        _logger = logger;
    }

    /// <summary>
    /// Получить активные сессии контрагента по FimBiz Contractor ID
    /// </summary>
    public async Task<GetActiveSessionsResponse> GetActiveSessionsByContractorIdAsync(int fimBizContractorId)
    {
        try
        {
            // Находим контрагента по FimBizContractorId
            var counterparty = await _counterpartyRepository.GetByFimBizIdAsync(fimBizContractorId);
            if (counterparty == null)
            {
                _logger.LogWarning("Контрагент с FimBizContractorId {ContractorId} не найден", fimBizContractorId);
                return new GetActiveSessionsResponse { Sessions = { } };
            }

            // Получаем все сессии контрагента (включая неактивные, чтобы FimBiz знал о всех)
            var sessions = await _context.Sessions
                .Include(s => s.UserAccount)
                .Where(s => s.UserAccount.CounterpartyId == counterparty.Id)
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var sessionInfos = sessions.Select(s => MapToSessionInfo(s)).ToList();

            _logger.LogInformation("Получено {Count} сессий для контрагента {ContractorId}", 
                sessionInfos.Count, fimBizContractorId);

            return new GetActiveSessionsResponse
            {
                Sessions = { sessionInfos }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении сессий для контрагента {ContractorId}", fimBizContractorId);
            return new GetActiveSessionsResponse { Sessions = { } };
        }
    }

    /// <summary>
    /// Преобразование локальной сессии в SessionInfo для FimBiz
    /// </summary>
    private SessionInfo MapToSessionInfo(Session session)
    {
        return new SessionInfo
        {
            SessionId = session.Id.ToString(),
            DeviceInfo = session.DeviceInfo ?? string.Empty,
            UserAgent = session.UserAgent ?? string.Empty,
            IpAddress = session.IpAddress ?? string.Empty,
            CreatedAt = new DateTimeOffset(session.CreatedAt).ToUnixTimeSeconds(),
            ExpiresAt = new DateTimeOffset(session.ExpiresAt).ToUnixTimeSeconds(),
            IsActive = session.IsActive && session.ExpiresAt > DateTime.UtcNow
        };
    }
}

