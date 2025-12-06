using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Modules.UserCabinet.Services;

/// <summary>
/// Сервис для выполнения команд управления сессиями от FimBiz
/// </summary>
public class SessionControlService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ILogger<SessionControlService> _logger;

    public SessionControlService(
        ApplicationDbContext dbContext,
        ILogger<SessionControlService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Выполнить команду управления сессиями и вернуть результат
    /// </summary>
    public async Task<ExecuteSessionControlResponse> ExecuteSessionControlAsync(
        SessionControl sessionControl,
        CancellationToken cancellationToken = default)
    {
        var response = new ExecuteSessionControlResponse
        {
            Success = false,
            Message = string.Empty,
            DisconnectedCount = 0
        };

        try
        {
            _logger.LogInformation(
                "Выполнение команды управления сессиями: ContractorId={ContractorId}, Action={Action} (значение: {ActionValue}), SessionIds={SessionIds}, Reason={Reason}",
                sessionControl.ContractorId,
                sessionControl.Action,
                (int)sessionControl.Action,
                sessionControl.SessionIds != null && sessionControl.SessionIds.Count > 0 
                    ? string.Join(", ", sessionControl.SessionIds) 
                    : "нет",
                sessionControl.Reason ?? "не указана");

            // Находим контрагента по FimBizContractorId
            var counterparty = await _dbContext.Counterparties
                .FirstOrDefaultAsync(c => c.FimBizContractorId == sessionControl.ContractorId, cancellationToken);

            if (counterparty == null)
            {
                response.Success = false;
                response.Message = $"Контрагент {sessionControl.ContractorId} не найден";
                _logger.LogWarning("Контрагент {ContractorId} не найден для управления сессиями", sessionControl.ContractorId);
                return response;
            }

            // Находим UserAccount для этого контрагента
            var userAccount = await _dbContext.UserAccounts
                .FirstOrDefaultAsync(u => u.CounterpartyId == counterparty.Id, cancellationToken);

            if (userAccount == null)
            {
                response.Success = false;
                response.Message = $"UserAccount не найден для контрагента {sessionControl.ContractorId}";
                _logger.LogWarning("UserAccount не найден для контрагента {ContractorId}", sessionControl.ContractorId);
                return response;
            }

            switch (sessionControl.Action)
            {
                case SessionAction.DeactivateAll:
                    return await DeactivateAllSessionsAsync(userAccount.Id, sessionControl.ContractorId, cancellationToken);

                case SessionAction.DeactivateById:
                    return await DeactivateSessionsByIdAsync(
                        userAccount.Id, 
                        sessionControl.ContractorId, 
                        sessionControl.SessionIds, 
                        cancellationToken);

                default:
                    response.Success = false;
                    response.Message = $"Неизвестное действие: {sessionControl.Action} (значение: {(int)sessionControl.Action})";
                    _logger.LogWarning("Неизвестное действие управления сессиями: {Action} (значение: {ActionValue}) для контрагента {ContractorId}",
                        sessionControl.Action, (int)sessionControl.Action, sessionControl.ContractorId);
                    return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выполнении команды управления сессиями для контрагента {ContractorId}", sessionControl.ContractorId);
            response.Success = false;
            response.Message = $"Ошибка при выполнении команды: {ex.Message}";
            return response;
        }
    }

    private async Task<ExecuteSessionControlResponse> DeactivateAllSessionsAsync(
        Guid userAccountId,
        int contractorId,
        CancellationToken cancellationToken)
    {
        var response = new ExecuteSessionControlResponse
        {
            Success = true,
            Message = string.Empty,
            DisconnectedCount = 0
        };

        // Деактивируем все активные сессии
        var allSessions = await _dbContext.Sessions
            .Where(s => s.UserAccountId == userAccountId && s.IsActive && s.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var session in allSessions)
        {
            session.IsActive = false;
            response.DisconnectedSessionIds.Add(session.Id.ToString());
        }

        if (allSessions.Any())
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            response.DisconnectedCount = allSessions.Count;
            response.Message = $"Деактивировано {allSessions.Count} активных сессий";
            _logger.LogInformation("Деактивировано {Count} активных сессий для контрагента {ContractorId}", 
                allSessions.Count, contractorId);
        }
        else
        {
            response.Message = "Не найдено активных сессий для деактивации";
            _logger.LogInformation("Не найдено активных сессий для деактивации у контрагента {ContractorId}", 
                contractorId);
        }

        return response;
    }

    private async Task<ExecuteSessionControlResponse> DeactivateSessionsByIdAsync(
        Guid userAccountId,
        int contractorId,
        IList<string>? sessionIds,
        CancellationToken cancellationToken)
    {
        var response = new ExecuteSessionControlResponse
        {
            Success = false,
            Message = string.Empty,
            DisconnectedCount = 0
        };

        if (sessionIds == null || sessionIds.Count == 0)
        {
            response.Message = "Список SessionIds пуст";
            _logger.LogWarning("Получена команда DeactivateById без списка SessionIds для контрагента {ContractorId}", 
                contractorId);
            return response;
        }

        // Валидируем и парсим SessionIds
        var validSessionIds = sessionIds
            .Where(id => Guid.TryParse(id, out _))
            .Select(Guid.Parse)
            .ToList();

        if (validSessionIds.Count == 0)
        {
            response.Message = $"Нет валидных SessionIds. Получены: {string.Join(", ", sessionIds)}";
            _logger.LogWarning("Нет валидных SessionIds для деактивации у контрагента {ContractorId}. Получены: {InvalidIds}", 
                contractorId, string.Join(", ", sessionIds));
            return response;
        }

        // Отмечаем невалидные ID как ошибки
        var invalidIds = sessionIds.Except(validSessionIds.Select(g => g.ToString())).ToList();
        foreach (var invalidId in invalidIds)
        {
            response.ErrorSessionIds.Add(invalidId);
        }

        if (invalidIds.Any())
        {
            _logger.LogWarning("Некоторые SessionIds невалидны и будут пропущены для контрагента {ContractorId}: {InvalidIds}",
                contractorId, string.Join(", ", invalidIds));
        }

        // Находим сессии
        var sessions = await _dbContext.Sessions
            .Where(s => s.UserAccountId == userAccountId && validSessionIds.Contains(s.Id))
            .ToListAsync(cancellationToken);

        // Деактивируем найденные сессии
        foreach (var session in sessions)
        {
            session.IsActive = false;
            response.DisconnectedSessionIds.Add(session.Id.ToString());
        }

        // Отмечаем не найденные сессии как ошибки
        var foundIds = sessions.Select(s => s.Id).ToList();
        var notFoundIds = validSessionIds.Except(foundIds).ToList();
        foreach (var notFoundId in notFoundIds)
        {
            response.ErrorSessionIds.Add(notFoundId.ToString());
        }

        if (sessions.Count == 0)
        {
            response.Message = $"Сессии не найдены. Запрошены: {string.Join(", ", validSessionIds)}";
            _logger.LogWarning(
                "Сессии не найдены для контрагента {ContractorId}. Запрошены: {RequestedIds}",
                contractorId,
                string.Join(", ", validSessionIds));
            return response;
        }

        if (notFoundIds.Any())
        {
            _logger.LogWarning("Не все сессии найдены для контрагента {ContractorId}. Не найдены: {NotFoundIds}",
                contractorId, string.Join(", ", notFoundIds));
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        response.Success = true;
        response.DisconnectedCount = sessions.Count;
        
        var messages = new List<string>();
        if (sessions.Count > 0)
        {
            messages.Add($"Деактивировано {sessions.Count} сессий");
        }
        if (notFoundIds.Any())
        {
            messages.Add($"Не найдено {notFoundIds.Count} сессий");
        }
        if (invalidIds.Any())
        {
            messages.Add($"Невалидных ID: {invalidIds.Count}");
        }
        
        response.Message = string.Join(". ", messages);
        
        _logger.LogInformation("Деактивировано {Count} сессий для контрагента {ContractorId} по запросу из FimBiz. Причина: {Reason}", 
            sessions.Count, contractorId, "не указана");

        return response;
    }
}

