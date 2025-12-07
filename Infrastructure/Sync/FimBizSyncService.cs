using Grpc.Core;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Sync;

public class FimBizSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FimBizSyncService> _logger;
    private readonly IConfiguration _configuration;

    public FimBizSyncService(
        IServiceProvider serviceProvider,
        ILogger<FimBizSyncService> logger,
        IConfiguration configuration)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Проверяем, включена ли автоматическая синхронизация
        var enableAutoSync = _configuration.GetValue<bool>("FimBiz:EnableAutoSync", true);
        if (!enableAutoSync)
        {
            _logger.LogInformation("Автоматическая синхронизация с FimBiz отключена");
            return;
        }

        _logger.LogInformation("FimBiz синхронизация запущена");

        // Сначала выполняем полную синхронизацию
        await PerformFullSyncAsync(stoppingToken);

        // Затем подписываемся на изменения
        await SubscribeToChangesAsync(stoppingToken);
    }

    private async Task PerformFullSyncAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var grpcClient = scope.ServiceProvider.GetRequiredService<IFimBizGrpcClient>();
        var counterpartyRepository = scope.ServiceProvider.GetRequiredService<ICounterpartyRepository>();
        var shopRepository = scope.ServiceProvider.GetRequiredService<IShopRepository>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            _logger.LogInformation("Начало полной синхронизации контрагентов");

            // Получаем все активные магазины
            var activeShops = await shopRepository.GetAllActiveAsync();
            
            if (!activeShops.Any())
            {
                _logger.LogWarning("Не найдено активных магазинов для синхронизации");
                return;
            }

            int totalSyncedCount = 0;

            // Синхронизируем контрагентов для каждого магазина
            foreach (var shop in activeShops)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                _logger.LogInformation("Синхронизация контрагентов для магазина {ShopName} (CompanyId: {CompanyId})", 
                    shop.Name, shop.FimBizCompanyId);

                // Получаем контрагентов для этого магазина
                // Синхронизируем только контрагентов с флагом создания кабинета
                var request = new GetContractorsRequest
                {
                    CompanyId = shop.FimBizCompanyId,
                    WithCorporatePhone = true,
                    WithCreateCabinet = true,  // Только контрагенты с флагом создания кабинета
                    BuyersOnly = true,
                    PageSize = 1000
                };
                
                if (shop.FimBizOrganizationId.HasValue)
                {
                    request.OrganizationId = shop.FimBizOrganizationId.Value;
                }

                var response = await grpcClient.GetContractorsAsync(request);
                
                int shopSyncedCount = 0;
                foreach (var contractor in response.Contractors)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    await SyncContractorAsync(contractor, counterpartyRepository, dbContext, cancellationToken);
                    shopSyncedCount++;
                }

                totalSyncedCount += shopSyncedCount;
                _logger.LogInformation("Синхронизировано {Count} контрагентов для магазина {ShopName}", 
                    shopSyncedCount, shop.Name);
            }

            _logger.LogInformation("Полная синхронизация завершена. Всего синхронизировано {Count} контрагентов для {ShopCount} магазинов", 
                totalSyncedCount, activeShops.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при полной синхронизации");
        }
    }

    private async Task SubscribeToChangesAsync(CancellationToken cancellationToken)
    {
        // Для каждого магазина создаем отдельную подписку
        // Запускаем их параллельно
        var tasks = new List<Task>();

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var shopRepository = scope.ServiceProvider.GetRequiredService<IShopRepository>();
                var activeShops = await shopRepository.GetAllActiveAsync();

                if (!activeShops.Any())
                {
                    _logger.LogWarning("Не найдено активных магазинов для подписки на изменения");
                    await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
                    continue;
                }

                // Очищаем завершенные задачи
                tasks.RemoveAll(t => t.IsCompleted);

                // Создаем подписку для каждого магазина
                foreach (var shop in activeShops)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    var shopTask = SubscribeToShopChangesAsync(shop, cancellationToken);
                    tasks.Add(shopTask);
                }

                // Ждем завершения всех подписок
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в подписке на изменения. Переподключение через 30 секунд...");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private async Task SubscribeToShopChangesAsync(Shop shop, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var grpcClient = scope.ServiceProvider.GetRequiredService<IFimBizGrpcClient>();
                var counterpartyRepository = scope.ServiceProvider.GetRequiredService<ICounterpartyRepository>();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Получаем последнюю версию синхронизации для этого магазина
                var lastSyncVersion = await GetLastSyncVersionAsync(dbContext, shop.FimBizCompanyId, shop.FimBizOrganizationId);

                var subscribeRequest = new SubscribeRequest
                {
                    CompanyId = shop.FimBizCompanyId,
                    LastSyncVersion = lastSyncVersion
                };
                
                if (shop.FimBizOrganizationId.HasValue)
                {
                    subscribeRequest.OrganizationId = shop.FimBizOrganizationId.Value;
                }

                _logger.LogInformation("Подписка на изменения контрагентов для магазина {ShopName} (CompanyId: {CompanyId}) с версии {LastSyncVersion}", 
                    shop.Name, shop.FimBizCompanyId, lastSyncVersion);

                using var call = grpcClient.SubscribeToChanges(subscribeRequest);
                
                await foreach (var change in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    await ProcessContractorChangeAsync(change, counterpartyRepository, dbContext, cancellationToken);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogInformation("Подписка для магазина {ShopName} отменена", shop.Name);
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в подписке на изменения для магазина {ShopName}. Переподключение через 30 секунд...", shop.Name);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private async Task ProcessContractorChangeAsync(
        ContractorChange change,
        ICounterpartyRepository counterpartyRepository,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            // Обработка управления сессиями (если есть)
            if (change.SessionControl != null)
            {
                _logger.LogInformation("Получено изменение контрагента с командой управления сессиями: ContractorId={ContractorId}, ChangeType={ChangeType}",
                    change.Contractor?.ContractorId ?? change.SessionControl.ContractorId,
                    change.ChangeType);
                await ProcessSessionControlAsync(change.SessionControl, dbContext, cancellationToken);
            }

            var contractor = change.Contractor;
            
            // Если контрагент отсутствует, выходим
            if (contractor == null)
            {
                _logger.LogWarning("Получено изменение контрагента без данных контрагента. ChangeType={ChangeType}", change.ChangeType);
                return;
            }
            
            // Если флаг is_create_cabinet = false, не синхронизируем контрагента (если его еще нет в БД)
            // Если контрагент уже есть, обновляем его и деактивируем кабинет
            if (!contractor.IsCreateCabinet && change.ChangeType == ContractorChangeType.Created)
            {
                _logger.LogInformation("Контрагент {ContractorId} пропущен, так как is_create_cabinet = false", contractor.ContractorId);
                return;
            }
            
            switch (change.ChangeType)
            {
                case ContractorChangeType.Created:
                case ContractorChangeType.Updated:
                    await SyncContractorAsync(contractor, counterpartyRepository, dbContext, cancellationToken);
                    _logger.LogInformation("Контрагент {ContractorId} {Action}", 
                        contractor.ContractorId, 
                        change.ChangeType == ContractorChangeType.Created ? "создан" : "обновлен");
                    break;

                case ContractorChangeType.Deleted:
                    await DeleteContractorAsync(contractor.ContractorId, counterpartyRepository, dbContext, cancellationToken);
                    _logger.LogInformation("Контрагент {ContractorId} удален", contractor.ContractorId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке изменения контрагента {ContractorId}", change.Contractor?.ContractorId);
        }
    }

    private async Task ProcessSessionControlAsync(
        SessionControl sessionControl,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "Получена команда управления сессиями: ContractorId={ContractorId}, Action={Action} (значение: {ActionValue}), SessionIds={SessionIds}, Reason={Reason}",
                sessionControl.ContractorId,
                sessionControl.Action,
                (int)sessionControl.Action,
                sessionControl.SessionIds != null && sessionControl.SessionIds.Count > 0 
                    ? string.Join(", ", sessionControl.SessionIds) 
                    : "нет",
                sessionControl.Reason ?? "не указана");

            // Находим контрагента по FimBizContractorId
            var counterparty = await dbContext.Counterparties
                .FirstOrDefaultAsync(c => c.FimBizContractorId == sessionControl.ContractorId, cancellationToken);

            if (counterparty == null)
            {
                _logger.LogWarning("Контрагент {ContractorId} не найден для управления сессиями", sessionControl.ContractorId);
                return;
            }

            // Находим UserAccount для этого контрагента
            var userAccount = await dbContext.UserAccounts
                .FirstOrDefaultAsync(u => u.CounterpartyId == counterparty.Id, cancellationToken);

            if (userAccount == null)
            {
                _logger.LogWarning("UserAccount не найден для контрагента {ContractorId}", sessionControl.ContractorId);
                return;
            }

            switch (sessionControl.Action)
            {
                case SessionAction.DisconnectAllSessions:
                    // Деактивируем все активные сессии
                    var allSessions = await dbContext.Sessions
                        .Where(s => s.UserAccountId == userAccount.Id && s.IsActive && s.ExpiresAt > DateTime.UtcNow)
                        .ToListAsync(cancellationToken);

                    foreach (var session in allSessions)
                    {
                        session.IsActive = false;
                    }

                    if (allSessions.Any())
                    {
                        await dbContext.SaveChangesAsync(cancellationToken);
                        _logger.LogInformation("Деактивировано {Count} активных сессий для контрагента {ContractorId} по запросу из FimBiz", 
                            allSessions.Count, sessionControl.ContractorId);
                    }
                    else
                    {
                        _logger.LogInformation("Не найдено активных сессий для деактивации у контрагента {ContractorId}", 
                            sessionControl.ContractorId);
                    }
                    break;

                case SessionAction.DisconnectSessions:
                    // Деактивируем конкретные сессии по ID
                    if (sessionControl.SessionIds == null || sessionControl.SessionIds.Count == 0)
                    {
                        _logger.LogWarning("Получена команда DisconnectSessions без списка SessionIds для контрагента {ContractorId}", 
                            sessionControl.ContractorId);
                        return;
                    }

                    var sessionIds = sessionControl.SessionIds
                        .Where(id => Guid.TryParse(id, out _))
                        .Select(Guid.Parse)
                        .ToList();

                    if (sessionIds.Count == 0)
                    {
                        _logger.LogWarning("Нет валидных SessionIds для деактивации у контрагента {ContractorId}. Получены: {InvalidIds}", 
                            sessionControl.ContractorId, 
                            string.Join(", ", sessionControl.SessionIds));
                        return;
                    }

                    if (sessionIds.Count < sessionControl.SessionIds.Count)
                    {
                        var invalidIds = sessionControl.SessionIds.Except(sessionIds.Select(g => g.ToString()));
                        _logger.LogWarning("Некоторые SessionIds невалидны и будут пропущены для контрагента {ContractorId}: {InvalidIds}",
                            sessionControl.ContractorId, string.Join(", ", invalidIds));
                    }

                    var sessions = await dbContext.Sessions
                        .Where(s => s.UserAccountId == userAccount.Id && sessionIds.Contains(s.Id))
                        .ToListAsync(cancellationToken);

                    if (sessions.Count == 0)
                    {
                        _logger.LogWarning(
                            "Сессии не найдены для контрагента {ContractorId}. Запрошены: {RequestedIds}",
                            sessionControl.ContractorId,
                            string.Join(", ", sessionIds));
                        return;
                    }

                    if (sessions.Count < sessionIds.Count)
                    {
                        var foundIds = sessions.Select(s => s.Id).ToList();
                        var notFoundIds = sessionIds.Except(foundIds);
                        _logger.LogWarning("Не все сессии найдены для контрагента {ContractorId}. Не найдены: {NotFoundIds}",
                            sessionControl.ContractorId, string.Join(", ", notFoundIds));
                    }

                    foreach (var session in sessions)
                    {
                        session.IsActive = false;
                    }

                    await dbContext.SaveChangesAsync(cancellationToken);
                    _logger.LogInformation("Деактивировано {Count} сессий для контрагента {ContractorId} по запросу из FimBiz. Причина: {Reason}", 
                        sessions.Count, sessionControl.ContractorId, sessionControl.Reason ?? "не указана");
                    break;

                default:
                    _logger.LogWarning("Неизвестное действие управления сессиями: {Action} (значение: {ActionValue}) для контрагента {ContractorId}",
                        sessionControl.Action, (int)sessionControl.Action, sessionControl.ContractorId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке управления сессиями для контрагента {ContractorId}", sessionControl.ContractorId);
        }
    }

    private async Task SyncContractorAsync(
        Contractor contractor,
        ICounterpartyRepository counterpartyRepository,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Найти существующего контрагента по FimBizContractorId
        var existing = await counterpartyRepository.GetByFimBizIdAsync(contractor.ContractorId);
        
        // Проверяем, изменился ли флаг is_create_cabinet с true на false
        bool wasCreateCabinetEnabled = existing?.IsCreateCabinet ?? false;
        bool isCreateCabinetEnabled = contractor.IsCreateCabinet;
        
        var counterparty = existing ?? new Counterparty
        {
            Id = Guid.NewGuid(),
            FimBizContractorId = contractor.ContractorId,
            CreatedAt = DateTime.UtcNow
        };

        // Обновляем данные
        counterparty.FimBizCompanyId = contractor.CompanyId > 0 ? contractor.CompanyId : null;
        counterparty.FimBizOrganizationId = contractor.OrganizationId > 0 ? contractor.OrganizationId : null;
        counterparty.Name = contractor.Name ?? string.Empty;
        counterparty.PhoneNumber = contractor.CorporatePhoneNumber ?? contractor.Phone ?? string.Empty;
        counterparty.Type = MapContractorType(contractor.Type);
        counterparty.Email = string.IsNullOrEmpty(contractor.Email) ? null : contractor.Email;
        counterparty.Inn = string.IsNullOrEmpty(contractor.Inn) ? null : contractor.Inn;
        counterparty.Kpp = string.IsNullOrEmpty(contractor.Kpp) ? null : contractor.Kpp;
        counterparty.LegalAddress = string.IsNullOrEmpty(contractor.Address) ? null : contractor.Address;
        counterparty.LastSyncVersion = contractor.SyncVersion > 0 ? contractor.SyncVersion : null;
        counterparty.IsCreateCabinet = contractor.IsCreateCabinet;
        counterparty.UpdatedAt = DateTime.UtcNow;

        if (existing == null)
        {
            await counterpartyRepository.CreateAsync(counterparty);
        }
        else
        {
            await counterpartyRepository.UpdateAsync(counterparty);
        }

        // Если флаг is_create_cabinet изменился с true на false, деактивируем все активные сессии
        if (wasCreateCabinetEnabled && !isCreateCabinetEnabled && existing != null)
        {
            await DeactivateUserSessionsAsync(counterparty.Id, dbContext, cancellationToken);
            _logger.LogWarning("Флаг is_create_cabinet для контрагента {ContractorId} изменен на false. Все активные сессии деактивированы.", 
                contractor.ContractorId);
        }

        // Синхронизируем скидки
        await SyncDiscountsAsync(contractor, counterparty, dbContext, cancellationToken);
    }

    private async Task SyncDiscountsAsync(
        Contractor contractor,
        Counterparty counterparty,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Удаляем старые скидки для этого контрагента
        var oldDiscounts = await dbContext.Discounts
            .Where(d => d.CounterpartyId == counterparty.Id)
            .ToListAsync(cancellationToken);
        
        if (oldDiscounts.Any())
        {
            dbContext.Discounts.RemoveRange(oldDiscounts);
        }

        // Добавляем новые скидки
        var now = DateTime.UtcNow;
        foreach (var rule in contractor.DiscountRules.Where(r => r.IsActive))
        {
            var validFrom = rule.HasValidFrom && rule.ValidFrom > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rule.ValidFrom).UtcDateTime
                : DateTime.UtcNow;

            var validTo = rule.HasValidTo && rule.ValidTo > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rule.ValidTo).UtcDateTime
                : (DateTime?)null;

            // Проверяем, что скидка еще действительна
            if (validFrom > now || (validTo.HasValue && validTo.Value < now))
                continue;

            var discount = new Discount
            {
                Id = Guid.NewGuid(),
                CounterpartyId = counterparty.Id,
                NomenclatureGroupId = rule.NomenclatureGroupId > 0
                    ? Guid.Parse(rule.NomenclatureGroupId.ToString())
                    : null,
                DiscountPercent = (decimal)rule.DiscountPercent,
                ValidFrom = validFrom,
                ValidTo = validTo,
                IsActive = true,
                CreatedAt = rule.DateCreate > 0 
                    ? DateTimeOffset.FromUnixTimeSeconds(rule.DateCreate).UtcDateTime 
                    : DateTime.UtcNow,
                UpdatedAt = rule.DateUpdate > 0 
                    ? DateTimeOffset.FromUnixTimeSeconds(rule.DateUpdate).UtcDateTime 
                    : DateTime.UtcNow
            };

            dbContext.Discounts.Add(discount);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task DeleteContractorAsync(
        int fimBizContractorId,
        ICounterpartyRepository counterpartyRepository,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var counterparty = await counterpartyRepository.GetByFimBizIdAsync(fimBizContractorId);
        if (counterparty != null)
        {
            // Удаляем связанные скидки
            var discounts = await dbContext.Discounts
                .Where(d => d.CounterpartyId == counterparty.Id)
                .ToListAsync();
            
            if (discounts.Any())
            {
                dbContext.Discounts.RemoveRange(discounts);
            }

            // Удаляем контрагента
            dbContext.Counterparties.Remove(counterparty);
            await dbContext.SaveChangesAsync(cancellationToken);
            
            _logger.LogWarning("Контрагент {ContractorId} удален из локальной БД", fimBizContractorId);
        }
    }

    private CounterpartyType MapContractorType(string? type)
    {
        return type?.ToLower() switch
        {
            "юридическое лицо" or "юр. лицо" or "юридическое" => CounterpartyType.B2B,
            "физическое лицо" or "физ. лицо" or "физическое" => CounterpartyType.B2C,
            _ => CounterpartyType.B2C
        };
    }

    private async Task<int> GetLastSyncVersionAsync(
        ApplicationDbContext dbContext,
        int? companyId,
        int? organizationId)
    {
        // Получаем максимальную версию синхронизации из контрагентов
        var query = dbContext.Counterparties.AsQueryable();

        if (companyId.HasValue && companyId.Value > 0)
        {
            query = query.Where(c => c.FimBizCompanyId == companyId);
        }

        if (organizationId.HasValue && organizationId.Value > 0)
        {
            query = query.Where(c => c.FimBizOrganizationId == organizationId);
        }

        var maxVersion = await query
            .Where(c => c.LastSyncVersion.HasValue)
            .MaxAsync(c => (int?)c.LastSyncVersion) ?? 0;

        return maxVersion;
    }

    private async Task DeactivateUserSessionsAsync(
        Guid counterpartyId,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Находим UserAccount для этого контрагента
        var userAccount = await dbContext.UserAccounts
            .FirstOrDefaultAsync(u => u.CounterpartyId == counterpartyId, cancellationToken);

        if (userAccount == null)
        {
            return; // Нет кабинета для этого контрагента
        }

        // Деактивируем все активные сессии
        var activeSessions = await dbContext.Sessions
            .Where(s => s.UserAccountId == userAccount.Id && s.IsActive && s.ExpiresAt > DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var session in activeSessions)
        {
            session.IsActive = false;
        }

        if (activeSessions.Any())
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("Деактивировано {Count} активных сессий для контрагента {CounterpartyId}", 
                activeSessions.Count, counterpartyId);
        }
    }
}

