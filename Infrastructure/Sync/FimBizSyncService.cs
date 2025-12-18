using System.Text.Json;
using Grpc.Core;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OrderStatus = InternetShopService_back.Modules.OrderManagement.Models.OrderStatus;
using GrpcOrderStatus = InternetShopService_back.Infrastructure.Grpc.Orders.OrderStatus;
using GrpcDeliveryType = InternetShopService_back.Infrastructure.Grpc.Orders.DeliveryType;
using LocalDeliveryType = InternetShopService_back.Modules.OrderManagement.Models.DeliveryType;
using LocalOrder = InternetShopService_back.Modules.OrderManagement.Models.Order;
using LocalOrderItem = InternetShopService_back.Modules.OrderManagement.Models.OrderItem;
using GrpcOrder = InternetShopService_back.Infrastructure.Grpc.Orders.Order;
using GrpcOrderItem = InternetShopService_back.Infrastructure.Grpc.Orders.OrderItem;

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

        // Запускаем периодическую синхронизацию заказов
        var syncOrdersTask = SyncOrdersPeriodicallyAsync(stoppingToken);

        // Затем подписываемся на изменения контрагентов
        var subscribeTask = SubscribeToChangesAsync(stoppingToken);

        // Ждем завершения обеих задач
        await Task.WhenAll(syncOrdersTask, subscribeTask);
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
                foreach (var contractorSummary in response.Contractors)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Логируем количество DiscountRules в базовом ответе GetContractors
                    // Согласно прото файлу FimBiz, GetContractors может не возвращать полные данные
                    _logger.LogDebug("Базовые данные контрагента {ContractorId} из GetContractors: DiscountRules.Count = {Count}", 
                        contractorSummary.ContractorId, contractorSummary.DiscountRules?.Count ?? 0);

                    // Получаем полные данные контрагента со скидками через GetContractor (rpc GetContractor)
                    // Это гарантирует получение всех discount_rules согласно прото файлу FimBiz
                    var fullContractor = await grpcClient.GetContractorGrpcAsync(contractorSummary.ContractorId);
                    if (fullContractor != null)
                    {
                        _logger.LogInformation("Полные данные контрагента {ContractorId} из GetContractor: DiscountRules.Count = {Count}", 
                            fullContractor.ContractorId, fullContractor.DiscountRules?.Count ?? 0);
                        
                        // SyncContractorAsync принимает Contractor из gRPC (тип из прото файла)
                        await SyncContractorAsync(fullContractor, counterpartyRepository, dbContext, cancellationToken);
                    }
                    else
                    {
                        _logger.LogWarning("Не удалось получить полные данные контрагента {ContractorId} через GetContractor, используем базовые данные из GetContractors", 
                            contractorSummary.ContractorId);
                        await SyncContractorAsync(contractorSummary, counterpartyRepository, dbContext, cancellationToken);
                    }
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
                
                _logger.LogInformation("=== [CONTRACTOR] ПОДПИСКА АКТИВНА, ОЖИДАЕМ ИЗМЕНЕНИЯ ===");
                
                await foreach (var change in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    _logger.LogInformation("=== [CONTRACTOR] ПОЛУЧЕН СТРИМ ИЗМЕНЕНИЙ ===");
                    await ProcessContractorChangeAsync(change, counterpartyRepository, dbContext, cancellationToken);
                }
                
                _logger.LogWarning("=== [CONTRACTOR] СТРИМ ЗАВЕРШЕН (подписка прервана) ===");
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogInformation("Подписка для магазина {ShopName} отменена", shop.Name);
                break;
            }
            catch (RpcException ex)
            {
                _logger.LogError(ex, "=== [CONTRACTOR] ОШИБКА gRPC ПРИ ПОДПИСКЕ ===");
                _logger.LogError("StatusCode: {StatusCode}, Detail: {Detail}, Message: {Message}", 
                    ex.StatusCode, ex.Status.Detail, ex.Message);
                _logger.LogError("Ошибка в подписке на изменения для магазина {ShopName}. Переподключение через 30 секунд...", shop.Name);
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "=== [CONTRACTOR] НЕОЖИДАННАЯ ОШИБКА ПРИ ПОДПИСКЕ ===");
                _logger.LogError("Ошибка в подписке на изменения для магазина {ShopName}. Переподключение через 30 секунд...", shop.Name);
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
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ =====
        _logger.LogInformation("=== [CONTRACTOR] ПОЛУЧЕНО ИЗМЕНЕНИЕ ОТ FIMBIZ ===");
        _logger.LogInformation("ChangeType: {ChangeType}", change.ChangeType);
        _logger.LogInformation("ContractorId: {ContractorId}", change.Contractor?.ContractorId ?? 0);
        _logger.LogInformation("Contractor.Name: {Name}", change.Contractor?.Name ?? "NULL");
        _logger.LogInformation("HasSessionControl: {HasSessionControl}", change.SessionControl != null);
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

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
            
            // Логируем количество DiscountRules в изменении из стрима SubscribeToChanges
            // Согласно прото файлу FimBiz, ContractorChange содержит Contractor с discount_rules
            _logger.LogInformation("Изменение контрагента {ContractorId} из стрима: DiscountRules.Count = {Count}", 
                contractor.ContractorId, contractor.DiscountRules?.Count ?? 0);

            // Получаем полные данные контрагента со скидками через GetContractor для гарантии
            // Это важно, так как стрим может передавать неполные данные для оптимизации
            using var scope = _serviceProvider.CreateScope();
            var grpcClient = scope.ServiceProvider.GetRequiredService<IFimBizGrpcClient>();
            var fullContractor = await grpcClient.GetContractorGrpcAsync(contractor.ContractorId);
            if (fullContractor != null)
            {
                _logger.LogInformation("Полные данные контрагента {ContractorId} из GetContractor: DiscountRules.Count = {Count}", 
                    fullContractor.ContractorId, fullContractor.DiscountRules?.Count ?? 0);
                contractor = fullContractor; // Используем полные данные со скидками
            }
            else
            {
                _logger.LogWarning("Не удалось получить полные данные контрагента {ContractorId} из GetContractor, используем данные из стрима SubscribeToChanges", 
                    contractor.ContractorId);
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
        counterparty.HasPostPayment = contractor.IsPostPayment;
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

        // Логируем информацию о DiscountRules перед синхронизацией
        _logger.LogInformation(
            "Контрагент {ContractorId}: DiscountRules.Count = {Count}",
            contractor.ContractorId, contractor.DiscountRules?.Count ?? 0);

        // Синхронизируем скидки
        await SyncDiscountsAsync(contractor, counterparty, dbContext, cancellationToken);
    }

    private async Task SyncDiscountsAsync(
        Contractor contractor,
        Counterparty counterparty,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        // Логируем количество полученных скидок
        _logger.LogInformation("=== [DISCOUNT SYNC] Начало синхронизации скидок ===");
        _logger.LogInformation("ContractorId (local): {LocalId}, FimBizContractorId: {FimBizId}", 
            counterparty.Id, contractor.ContractorId);
        _logger.LogInformation(
            "Синхронизация скидок для контрагента {ContractorId} (FimBiz ID: {FimBizContractorId}). Получено DiscountRules: {Count}",
            counterparty.Id, contractor.ContractorId, contractor.DiscountRules.Count);

        // Логируем каждую DiscountRule с деталями
        for (int i = 0; i < contractor.DiscountRules.Count; i++)
        {
            var rule = contractor.DiscountRules[i];
            _logger.LogInformation("DiscountRule[{Index}]: ID={Id}, Name={Name}, Percent={Percent}%, IsActive={IsActive}, GroupId={GroupId}, NomenclatureId={NomenclatureId}",
                i, rule.Id, rule.Name ?? "null", rule.DiscountPercent, rule.IsActive,
                rule.NomenclatureGroupId > 0 ? rule.NomenclatureGroupId.ToString() : "null",
                rule.HasNomenclatureId && rule.NomenclatureId > 0 ? rule.NomenclatureId.ToString() : "null");
        }

        // Удаляем старые скидки для этого контрагента
        var oldDiscounts = await dbContext.Discounts
            .Where(d => d.CounterpartyId == counterparty.Id)
            .ToListAsync(cancellationToken);
        
        if (oldDiscounts.Any())
        {
            _logger.LogInformation("Удаление {Count} старых скидок для контрагента {ContractorId}", 
                oldDiscounts.Count, counterparty.Id);
            dbContext.Discounts.RemoveRange(oldDiscounts);
        }

        // Добавляем новые скидки
        var now = DateTime.UtcNow;
        int addedCount = 0;
        int skippedCount = 0;
        
        foreach (var rule in contractor.DiscountRules)
        {
            // Логируем все правила, даже неактивные
            if (!rule.IsActive)
            {
                _logger.LogInformation("Пропущена неактивная скидка ID={DiscountId}, Name={Name} для контрагента {ContractorId}",
                    rule.Id, rule.Name ?? "без названия", contractor.ContractorId);
                skippedCount++;
                continue;
            }

            // Если HasValidFrom=False, это означает бессрочную скидку (действует с начала времен)
            // Если HasValidTo=False, это означает бессрочную скидку (без даты окончания)
            var validFrom = rule.HasValidFrom && rule.ValidFrom > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rule.ValidFrom).UtcDateTime
                : DateTime.MinValue; // Бессрочная скидка - действует с начала времен

            var validTo = rule.HasValidTo && rule.ValidTo > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rule.ValidTo).UtcDateTime
                : (DateTime?)null; // Бессрочная скидка - без даты окончания

            // Логируем даты для диагностики
            _logger.LogInformation("Обработка скидки ID={DiscountId}: HasValidFrom={HasValidFrom}, ValidFrom={ValidFrom}, HasValidTo={HasValidTo}, ValidTo={ValidTo}, Now={Now}",
                rule.Id, rule.HasValidFrom, validFrom, rule.HasValidTo, validTo?.ToString() ?? "null", now);

            // Проверяем, что скидка еще действительна
            // Для бессрочных скидок (validFrom = DateTime.MinValue и validTo = null) проверка всегда проходит
            if (validFrom > now || (validTo.HasValue && validTo.Value < now))
            {
                _logger.LogInformation("Пропущена скидка ID={DiscountId} (недействительна по датам) для контрагента {ContractorId}. ValidFrom={ValidFrom}, ValidTo={ValidTo}, Now={Now}",
                    rule.Id, contractor.ContractorId, validFrom, validTo?.ToString() ?? "null", now);
                skippedCount++;
                continue;
            }

            try
            {
                Guid? nomenclatureGroupIdGuid = rule.NomenclatureGroupId > 0
                    ? ConvertInt32ToGuid(rule.NomenclatureGroupId)
                    : null;
                Guid? nomenclatureIdGuid = rule.HasNomenclatureId && rule.NomenclatureId > 0
                    ? ConvertInt32ToGuid(rule.NomenclatureId)
                    : null;

                var discount = new Discount
                {
                    Id = Guid.NewGuid(),
                    CounterpartyId = counterparty.Id,
                    NomenclatureGroupId = nomenclatureGroupIdGuid,
                    NomenclatureId = nomenclatureIdGuid,
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
                addedCount++;
                
                _logger.LogInformation(
                    "Добавлена скидка ID={DiscountId}, Percent={Percent}%, " +
                    "NomenclatureGroupId: FimBiz={GroupIdFimBiz}, Guid={GroupIdGuid}, " +
                    "NomenclatureId: FimBiz={NomenclatureIdFimBiz}, Guid={NomenclatureIdGuid} " +
                    "для контрагента {ContractorId}",
                    rule.Id, rule.DiscountPercent, 
                    rule.NomenclatureGroupId > 0 ? rule.NomenclatureGroupId.ToString() : "null",
                    nomenclatureGroupIdGuid?.ToString() ?? "null",
                    rule.HasNomenclatureId && rule.NomenclatureId > 0 ? rule.NomenclatureId.ToString() : "null",
                    nomenclatureIdGuid?.ToString() ?? "null",
                    contractor.ContractorId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке скидки ID={DiscountId} для контрагента {ContractorId}. NomenclatureGroupId={GroupId}, NomenclatureId={NomenclatureId}", 
                    rule.Id, contractor.ContractorId,
                    rule.NomenclatureGroupId > 0 ? rule.NomenclatureGroupId.ToString() : "null",
                    rule.HasNomenclatureId && rule.NomenclatureId > 0 ? rule.NomenclatureId.ToString() : "null");
                skippedCount++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        
        _logger.LogInformation(
            "Синхронизация скидок завершена для контрагента {ContractorId}. Добавлено: {AddedCount}, Пропущено: {SkippedCount}",
            counterparty.Id, addedCount, skippedCount);
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

    /// <summary>
    /// Периодическая синхронизация заказов с FimBiz
    /// </summary>
    private async Task SyncOrdersPeriodicallyAsync(CancellationToken cancellationToken)
    {
        var syncIntervalMinutes = _configuration.GetValue<int>("FimBiz:SyncIntervalMinutes", 60);
        var syncInterval = TimeSpan.FromMinutes(syncIntervalMinutes);

        _logger.LogInformation("Запущена периодическая синхронизация заказов. Интервал: {IntervalMinutes} минут", syncIntervalMinutes);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(syncInterval, cancellationToken);

                _logger.LogInformation("Начало периодической синхронизации заказов");

                using var scope = _serviceProvider.CreateScope();
                var grpcClient = scope.ServiceProvider.GetRequiredService<IFimBizGrpcClient>();
                var orderRepository = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
                var shopRepository = scope.ServiceProvider.GetRequiredService<IShopRepository>();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Получаем все активные магазины
                var activeShops = await shopRepository.GetAllActiveAsync();

                if (!activeShops.Any())
                {
                    _logger.LogWarning("Не найдено активных магазинов для синхронизации заказов");
                    continue;
                }

                int totalSyncedCount = 0;

                // Синхронизируем заказы для каждого магазина
                foreach (var shop in activeShops)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    try
                    {
                        // Получаем заказы, которые синхронизированы с FimBiz (имеют FimBizOrderId)
                        var syncedOrders = await dbContext.Orders
                            .Where(o => o.FimBizOrderId.HasValue && o.FimBizOrderId.Value > 0)
                            .OrderByDescending(o => o.UpdatedAt)
                            .Take(100) // Синхронизируем последние 100 заказов
                            .ToListAsync(cancellationToken);

                        _logger.LogInformation("Найдено {Count} заказов для синхронизации для магазина {ShopName}", 
                            syncedOrders.Count, shop.Name);

                        foreach (var order in syncedOrders)
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            try
                            {
                                await SyncOrderFromFimBizAsync(order, shop, grpcClient, orderRepository, dbContext, cancellationToken);
                                totalSyncedCount++;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Ошибка при синхронизации заказа {OrderId} из FimBiz", order.Id);
                                // Продолжаем синхронизацию других заказов
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при синхронизации заказов для магазина {ShopName}", shop.Name);
                    }
                }

                _logger.LogInformation("Периодическая синхронизация заказов завершена. Синхронизировано заказов: {Count}", totalSyncedCount);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Периодическая синхронизация заказов отменена");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в периодической синхронизации заказов. Повтор через {IntervalMinutes} минут", syncIntervalMinutes);
            }
        }
    }

    /// <summary>
    /// Синхронизация одного заказа из FimBiz
    /// </summary>
    private async Task SyncOrderFromFimBizAsync(
        LocalOrder localOrder,
        Shop shop,
        IFimBizGrpcClient grpcClient,
        IOrderRepository orderRepository,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        if (!localOrder.FimBizOrderId.HasValue)
        {
            return; // Заказ еще не синхронизирован с FimBiz
        }

        try
        {
            // Получаем заказ из FimBiz
            var getOrderRequest = new GetOrderRequest
            {
                ExternalOrderId = localOrder.Id.ToString(),
                CompanyId = shop.FimBizCompanyId
            };

            var grpcOrder = await grpcClient.GetOrderAsync(getOrderRequest);

            if (grpcOrder == null!)
            {
                _logger.LogWarning("Заказ {OrderId} не найден в FimBiz", localOrder.Id);
                return;
            }

            // Сохраняем старые значения для проверки изменений
            var oldStatus = localOrder.Status;
            var oldTotalAmount = localOrder.TotalAmount;
            var oldTrackingNumber = localOrder.TrackingNumber;
            var oldOrderNumber = localOrder.OrderNumber;
            var oldDeliveryType = localOrder.DeliveryType;
            var oldCarrier = localOrder.Carrier;
            var oldIsPriority = localOrder.IsPriority;
            var oldIsLongAssembling = localOrder.IsLongAssembling;

            // Обновляем поля заказа из FimBiz
            localOrder.FimBizOrderId = grpcOrder.OrderId;
            localOrder.OrderNumber = grpcOrder.OrderNumber;
            localOrder.Status = MapGrpcStatusToLocal(grpcOrder.Status);
            localOrder.TotalAmount = (decimal)grpcOrder.TotalPrice / 100; // Из копеек в рубли

            // Обновляем DeliveryType
            var newDeliveryType = MapGrpcDeliveryTypeToLocal(grpcOrder.DeliveryType);
            if (oldDeliveryType != newDeliveryType)
            {
                _logger.LogInformation("Синхронизация: обновлен DeliveryType заказа {OrderId} с {OldDeliveryType} на {NewDeliveryType}", 
                    localOrder.Id, oldDeliveryType, newDeliveryType);
            }
            localOrder.DeliveryType = newDeliveryType;

            if (grpcOrder.HasModifiedPrice)
            {
                localOrder.TotalAmount = (decimal)grpcOrder.ModifiedPrice / 100;
            }

            // Обновляем TrackingNumber
            localOrder.TrackingNumber = string.IsNullOrEmpty(grpcOrder.TrackingNumber) ? null : grpcOrder.TrackingNumber;

            // Обновляем Carrier
            localOrder.Carrier = string.IsNullOrEmpty(grpcOrder.Carrier) ? null : grpcOrder.Carrier;

            // Обновляем флаги
            localOrder.IsPriority = grpcOrder.IsPriority;
            localOrder.IsLongAssembling = grpcOrder.IsLongAssembling;

            // Обновляем даты событий (если переданы)
            if (grpcOrder.HasAssembledAt && grpcOrder.AssembledAt > 0)
            {
                localOrder.AssembledAt = DateTimeOffset.FromUnixTimeSeconds(grpcOrder.AssembledAt).UtcDateTime;
            }

            if (grpcOrder.HasShippedAt && grpcOrder.ShippedAt > 0)
            {
                localOrder.ShippedAt = DateTimeOffset.FromUnixTimeSeconds(grpcOrder.ShippedAt).UtcDateTime;
            }

            if (grpcOrder.HasDeliveredAt && grpcOrder.DeliveredAt > 0)
            {
                localOrder.DeliveredAt = DateTimeOffset.FromUnixTimeSeconds(grpcOrder.DeliveredAt).UtcDateTime;
            }

            // Проверяем, были ли реальные изменения
            bool hasChanges = oldStatus != localOrder.Status
                || oldTotalAmount != localOrder.TotalAmount
                || oldTrackingNumber != localOrder.TrackingNumber
                || oldOrderNumber != localOrder.OrderNumber
                || oldDeliveryType != localOrder.DeliveryType
                || oldCarrier != localOrder.Carrier
                || oldIsPriority != localOrder.IsPriority
                || oldIsLongAssembling != localOrder.IsLongAssembling
                || grpcOrder.Items != null && grpcOrder.Items.Count > 0;

            if (hasChanges)
            {
                localOrder.SyncedWithFimBizAt = DateTime.UtcNow;
                localOrder.UpdatedAt = DateTime.UtcNow;

                // Добавляем запись в историю статусов только если статус изменился
                if (oldStatus != localOrder.Status)
                {
                    var statusHistory = new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = localOrder.Id,
                        Status = localOrder.Status,
                        ChangedAt = grpcOrder.StatusChangedAt > 0 
                            ? DateTimeOffset.FromUnixTimeSeconds(grpcOrder.StatusChangedAt).UtcDateTime 
                            : DateTime.UtcNow
                    };
                    localOrder.StatusHistory.Add(statusHistory);
                }

                // Синхронизируем позиции заказа, если они переданы
                if (grpcOrder.Items != null && grpcOrder.Items.Count > 0)
                {
                    await SyncOrderItemsAsync(localOrder, grpcOrder.Items, dbContext, cancellationToken);
                }

                await orderRepository.UpdateAsync(localOrder);

                _logger.LogInformation("Заказ {OrderId} успешно синхронизирован с FimBiz", localOrder.Id);
            }
            else
            {
                _logger.LogDebug("Заказ {OrderId} не изменился, пропускаем обновление", localOrder.Id);
            }
        }
        catch (RpcException ex) when (ex.StatusCode == StatusCode.NotFound)
        {
            _logger.LogWarning("Заказ {OrderId} не найден в FimBiz (404)", localOrder.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при синхронизации заказа {OrderId} из FimBiz", localOrder.Id);
            throw;
        }
    }

    /// <summary>
    /// Синхронизация позиций заказа
    /// </summary>
    private async Task SyncOrderItemsAsync(
        LocalOrder order,
        IEnumerable<GrpcOrderItem> grpcItems,
        ApplicationDbContext dbContext,
        CancellationToken cancellationToken)
    {
        try
        {
            // Удаляем старые позиции
            var existingItems = await dbContext.OrderItems
                .Where(i => i.OrderId == order.Id)
                .ToListAsync(cancellationToken);

            dbContext.OrderItems.RemoveRange(existingItems);

            // Добавляем новые позиции из FimBiz
            foreach (var grpcItem in grpcItems)
            {
                var orderItem = new LocalOrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    NomenclatureId = grpcItem.HasNomenclatureId && grpcItem.NomenclatureId > 0
                        ? ConvertInt32ToGuid(grpcItem.NomenclatureId)
                        : Guid.NewGuid(), // Генерируем новый GUID если нет NomenclatureId
                    NomenclatureName = grpcItem.Name,
                    Quantity = grpcItem.Quantity,
                    Price = (decimal)grpcItem.Price / 100, // Из копеек в рубли
                    DiscountPercent = 0, // TODO: получить из FimBiz если доступно
                    TotalAmount = (decimal)grpcItem.Price / 100 * grpcItem.Quantity,
                    UrlPhotosJson = SerializeUrlPhotos(grpcItem.PhotoUrls.ToList()),
                    CreatedAt = DateTime.UtcNow
                };

                await dbContext.OrderItems.AddAsync(orderItem, cancellationToken);
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Синхронизировано {Count} позиций для заказа {OrderId}", 
                grpcItems.Count(), order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при синхронизации позиций заказа {OrderId}", order.Id);
            // Не прерываем выполнение, просто логируем ошибку
        }
    }

    /// <summary>
    /// Сериализация списка URL фотографий в JSON строку
    /// </summary>
    private static string? SerializeUrlPhotos(List<string>? urlPhotos)
    {
        if (urlPhotos == null || !urlPhotos.Any())
        {
            return null;
        }

        try
        {
            return JsonSerializer.Serialize(urlPhotos);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Преобразование int32 в Guid (для обратной совместимости с FimBiz ID)
    /// Формат Guid: "00000000-0000-0000-0000-000000000167" где 167 - это значение int32
    /// </summary>
    private static Guid ConvertInt32ToGuid(int value)
    {
        // Создаем массив из 16 байт (размер Guid)
        var bytes = new byte[16];
        
        // Заполняем первые 12 байт нулями
        // Индексы 0-11 остаются нулями
        
        // Помещаем значение int32 в последние 4 байта (индексы 12-15)
        // Используем little-endian порядок байтов (стандарт для .NET)
        var int32Bytes = BitConverter.GetBytes(value);
        Array.Copy(int32Bytes, 0, bytes, 12, 4);
        
        return new Guid(bytes);
    }

    /// <summary>
    /// Преобразование статуса из gRPC в локальный enum
    /// </summary>
    private static OrderStatus MapGrpcStatusToLocal(GrpcOrderStatus grpcStatus)
    {
        return grpcStatus switch
        {
            GrpcOrderStatus.Processing => OrderStatus.Processing,
            GrpcOrderStatus.WaitingForPayment => OrderStatus.AwaitingPayment,
            GrpcOrderStatus.PaymentConfirmed => OrderStatus.InvoiceConfirmed,
            GrpcOrderStatus.Manufacturing => OrderStatus.Manufacturing,
            GrpcOrderStatus.Picking => OrderStatus.Assembling,
            GrpcOrderStatus.TransferredToTransport => OrderStatus.TransferredToCarrier,
            GrpcOrderStatus.DeliveringByTransport => OrderStatus.DeliveringByCarrier,
            GrpcOrderStatus.Delivering => OrderStatus.Delivering,
            GrpcOrderStatus.AwaitingPickup => OrderStatus.AwaitingPickup,
            GrpcOrderStatus.Completed => OrderStatus.Received,
            GrpcOrderStatus.Cancelled => OrderStatus.Cancelled,
            _ => OrderStatus.Processing // По умолчанию
        };
    }

    /// <summary>
    /// Преобразование типа доставки из gRPC в локальный enum
    /// </summary>
    private static LocalDeliveryType MapGrpcDeliveryTypeToLocal(GrpcDeliveryType grpcDeliveryType)
    {
        return grpcDeliveryType switch
        {
            GrpcDeliveryType.SelfPickup => LocalDeliveryType.Pickup,
            GrpcDeliveryType.CompanyDelivery => LocalDeliveryType.SellerDelivery,
            GrpcDeliveryType.TransportCompany => LocalDeliveryType.Carrier,
            _ => LocalDeliveryType.Pickup // По умолчанию самовывоз
        };
    }
}

