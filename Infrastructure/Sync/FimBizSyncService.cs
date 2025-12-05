using Grpc.Core;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
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
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        try
        {
            _logger.LogInformation("Начало полной синхронизации контрагентов");

            var companyId = _configuration.GetValue<int?>("FimBiz:CompanyId");
            var organizationId = _configuration.GetValue<int?>("FimBiz:OrganizationId");

            // Получаем всех контрагентов с корпоративным телефоном
            var request = new GetContractorsRequest
            {
                WithCorporatePhone = true,
                BuyersOnly = true,
                PageSize = 1000
            };
            
            if (companyId.HasValue && companyId.Value > 0)
            {
                request.CompanyId = companyId.Value;
            }
            
            if (organizationId.HasValue && organizationId.Value > 0)
            {
                request.OrganizationId = organizationId.Value;
            }

            var response = await grpcClient.GetContractorsAsync(request);
            
            int syncedCount = 0;
            foreach (var contractor in response.Contractors)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                await SyncContractorAsync(contractor, counterpartyRepository, dbContext, cancellationToken);
                syncedCount++;
            }

            _logger.LogInformation("Полная синхронизация завершена. Синхронизировано {Count} контрагентов", syncedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при полной синхронизации");
        }
    }

    private async Task SubscribeToChangesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var grpcClient = scope.ServiceProvider.GetRequiredService<IFimBizGrpcClient>();
                var counterpartyRepository = scope.ServiceProvider.GetRequiredService<ICounterpartyRepository>();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var companyId = _configuration.GetValue<int?>("FimBiz:CompanyId");
                var organizationId = _configuration.GetValue<int?>("FimBiz:OrganizationId");

                // Получаем последнюю версию синхронизации из БД
                var lastSyncVersion = await GetLastSyncVersionAsync(dbContext, companyId, organizationId);

                var subscribeRequest = new SubscribeRequest
                {
                    LastSyncVersion = lastSyncVersion
                };
                
                if (companyId.HasValue && companyId.Value > 0)
                {
                    subscribeRequest.CompanyId = companyId.Value;
                }
                
                if (organizationId.HasValue && organizationId.Value > 0)
                {
                    subscribeRequest.OrganizationId = organizationId.Value;
                }

                _logger.LogInformation("Подписка на изменения контрагентов с версии {LastSyncVersion}", lastSyncVersion);

                var call = grpcClient.SubscribeToChanges(subscribeRequest);
                
                await foreach (var change in call.ResponseStream.ReadAllAsync(cancellationToken))
                {
                    await ProcessContractorChangeAsync(change, counterpartyRepository, dbContext);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                _logger.LogInformation("Подписка отменена");
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка в подписке на изменения. Переподключение через 30 секунд...");
                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }
    }

    private async Task ProcessContractorChangeAsync(
        ContractorChange change,
        ICounterpartyRepository counterpartyRepository,
        ApplicationDbContext dbContext)
    {
        try
        {
            var contractor = change.Contractor;
            
            switch (change.ChangeType)
            {
                case ContractorChangeType.Created:
                case ContractorChangeType.Updated:
                    await SyncContractorAsync(contractor, counterpartyRepository, dbContext, CancellationToken.None);
                    _logger.LogInformation("Контрагент {ContractorId} {Action}", 
                        contractor.ContractorId, 
                        change.ChangeType == ContractorChangeType.Created ? "создан" : "обновлен");
                    break;

                case ContractorChangeType.Deleted:
                    await DeleteContractorAsync(contractor.ContractorId, counterpartyRepository, dbContext);
                    _logger.LogInformation("Контрагент {ContractorId} удален", contractor.ContractorId);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке изменения контрагента {ContractorId}", change.Contractor?.ContractorId);
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
        counterparty.UpdatedAt = DateTime.UtcNow;

        if (existing == null)
        {
            await counterpartyRepository.CreateAsync(counterparty);
        }
        else
        {
            await counterpartyRepository.UpdateAsync(counterparty);
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
        ApplicationDbContext dbContext)
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
            await dbContext.SaveChangesAsync();
            
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
}

