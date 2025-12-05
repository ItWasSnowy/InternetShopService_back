using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class CounterpartyService : ICounterpartyService
{
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly IFimBizGrpcClient _fimBizGrpcClient;
    private readonly ILogger<CounterpartyService> _logger;

    public CounterpartyService(
        IUserAccountRepository userAccountRepository,
        ICounterpartyRepository counterpartyRepository,
        IFimBizGrpcClient fimBizGrpcClient,
        ILogger<CounterpartyService> logger)
    {
        _userAccountRepository = userAccountRepository;
        _counterpartyRepository = counterpartyRepository;
        _fimBizGrpcClient = fimBizGrpcClient;
        _logger = logger;
    }

    public async Task<CounterpartyDto> GetCurrentCounterpartyAsync(Guid userId)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
        {
            throw new InvalidOperationException("Пользователь не найден");
        }

        var counterparty = await _counterpartyRepository.GetByIdAsync(userAccount.CounterpartyId);
        if (counterparty == null)
        {
            throw new InvalidOperationException("Контрагент не найден");
        }

        return MapToCounterpartyDto(counterparty);
    }

    public async Task<List<DiscountDto>> GetDiscountsAsync(Guid counterpartyId)
    {
        var discounts = await _counterpartyRepository.GetActiveDiscountsAsync(counterpartyId);
        
        return discounts.Select(d => new DiscountDto
        {
            Id = d.Id,
            NomenclatureGroupId = d.NomenclatureGroupId,
            NomenclatureId = d.NomenclatureId,
            DiscountPercent = d.DiscountPercent,
            ValidFrom = d.ValidFrom,
            ValidTo = d.ValidTo,
            IsActive = d.IsActive
        }).ToList();
    }

    public async Task SyncCounterpartyDataAsync(Guid counterpartyId)
    {
        try
        {
            _logger.LogInformation("Начало синхронизации данных контрагента {CounterpartyId}", counterpartyId);

            // Получаем данные из FimBiz
            var fimBizCounterparty = await _fimBizGrpcClient.GetCounterpartyByIdAsync(counterpartyId);
            if (fimBizCounterparty == null)
            {
                throw new InvalidOperationException("Контрагент не найден в FimBiz");
            }

            // Получаем локальные данные
            var localCounterparty = await _counterpartyRepository.GetByIdAsync(counterpartyId);
            if (localCounterparty == null)
            {
                // Создаем нового контрагента
                localCounterparty = new Counterparty
                {
                    Id = counterpartyId,
                    Name = fimBizCounterparty.Name,
                    PhoneNumber = fimBizCounterparty.PhoneNumber,
                    Type = fimBizCounterparty.Type,
                    Email = fimBizCounterparty.Email,
                    Inn = fimBizCounterparty.Inn,
                    Kpp = fimBizCounterparty.Kpp,
                    LegalAddress = fimBizCounterparty.LegalAddress,
                    EdoIdentifier = fimBizCounterparty.EdoIdentifier,
                    HasPostPayment = fimBizCounterparty.HasPostPayment,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _counterpartyRepository.CreateAsync(localCounterparty);
            }
            else
            {
                // Обновляем существующего контрагента
                localCounterparty.Name = fimBizCounterparty.Name;
                localCounterparty.PhoneNumber = fimBizCounterparty.PhoneNumber;
                localCounterparty.Type = fimBizCounterparty.Type;
                localCounterparty.Email = fimBizCounterparty.Email;
                localCounterparty.Inn = fimBizCounterparty.Inn;
                localCounterparty.Kpp = fimBizCounterparty.Kpp;
                localCounterparty.LegalAddress = fimBizCounterparty.LegalAddress;
                localCounterparty.EdoIdentifier = fimBizCounterparty.EdoIdentifier;
                localCounterparty.HasPostPayment = fimBizCounterparty.HasPostPayment;
                localCounterparty.UpdatedAt = DateTime.UtcNow;
                await _counterpartyRepository.UpdateAsync(localCounterparty);
            }

            // Синхронизируем скидки
            if (localCounterparty.FimBizContractorId.HasValue)
            {
                var fimBizDiscounts = await _fimBizGrpcClient.GetCounterpartyDiscountsAsync(localCounterparty.FimBizContractorId.Value);
                if (fimBizDiscounts != null && fimBizDiscounts.Any())
                {
                    // Здесь можно добавить логику синхронизации скидок в БД
                    // Пока просто логируем
                    _logger.LogInformation("Получено {Count} скидок из FimBiz для контрагента {CounterpartyId}", 
                        fimBizDiscounts.Count, counterpartyId);
                }
            }

            _logger.LogInformation("Синхронизация данных контрагента {CounterpartyId} завершена", counterpartyId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при синхронизации данных контрагента {CounterpartyId}", counterpartyId);
            throw;
        }
    }

    private CounterpartyDto MapToCounterpartyDto(Counterparty counterparty)
    {
        return new CounterpartyDto
        {
            Id = counterparty.Id,
            Name = counterparty.Name,
            PhoneNumber = counterparty.PhoneNumber,
            Type = counterparty.Type,
            Email = counterparty.Email,
            Inn = counterparty.Inn,
            Kpp = counterparty.Kpp,
            LegalAddress = counterparty.LegalAddress,
            EdoIdentifier = counterparty.EdoIdentifier,
            HasPostPayment = counterparty.HasPostPayment
        };
    }
}
