using System.Diagnostics;
using Grpc.Net.Client;
using Grpc.Core;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Grpc;

public class FimBizGrpcClient : IFimBizGrpcClient, IDisposable
{
    private readonly GrpcChannel _channel;
    private readonly ContractorSyncService.ContractorSyncServiceClient _client;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FimBizGrpcClient> _logger;
    private readonly string _apiKey;
    private readonly int _companyId;
    private readonly int _organizationId;

    public FimBizGrpcClient(IConfiguration configuration, ILogger<FimBizGrpcClient> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var endpoint = _configuration["FimBiz:GrpcEndpoint"] 
            ?? throw new InvalidOperationException("FimBiz:GrpcEndpoint не настроен");
        _apiKey = _configuration["FimBiz:ApiKey"] 
            ?? throw new InvalidOperationException("FimBiz:ApiKey не настроен");
        _companyId = _configuration.GetValue<int>("FimBiz:CompanyId", 0);
        _organizationId = _configuration.GetValue<int>("FimBiz:OrganizationId", 0);

        // Создание gRPC канала
        _channel = GrpcChannel.ForAddress(endpoint, new GrpcChannelOptions
        {
            MaxReceiveMessageSize = 4 * 1024 * 1024, // 4 MB
            MaxSendMessageSize = 4 * 1024 * 1024
        });

        _client = new ContractorSyncService.ContractorSyncServiceClient(_channel);
    }

    public async Task<Counterparty?> GetCounterpartyAsync(string phoneNumber)
    {
        try
        {
            // Получаем список контрагентов с фильтром по корпоративному телефону
            var request = new GetContractorsRequest
            {
                WithCorporatePhone = true,
                BuyersOnly = true,
                PageSize = 1000 // Получаем всех с корпоративным телефоном
            };
            
            if (_companyId > 0)
            {
                request.CompanyId = _companyId;
            }
            
            if (_organizationId > 0)
            {
                request.OrganizationId = _organizationId;
            }

            var headers = CreateHeaders();
            var response = await _client.GetContractorsAsync(request, headers);

            // Ищем контрагента по номеру телефона
            // Номер может быть в формате +7XXXXXXXXXX или 7XXXXXXXXXX
            var normalizedPhone = NormalizePhoneNumber(phoneNumber);
            
            var contractor = response.Contractors
                .FirstOrDefault(c => 
                    !string.IsNullOrEmpty(c.CorporatePhoneNumber) &&
                    NormalizePhoneNumber(c.CorporatePhoneNumber) == normalizedPhone);

            if (contractor == null)
            {
                _logger.LogWarning("Контрагент с номером {PhoneNumber} не найден в FimBiz", phoneNumber);
                return null;
            }

            return MapToCounterparty(contractor);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при получении контрагента по номеру {PhoneNumber}", phoneNumber);
            
            if (ex.StatusCode == StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("Неверный API ключ для FimBiz");
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении контрагента по номеру {PhoneNumber}", phoneNumber);
            return null;
        }
    }

    public async Task<Counterparty?> GetCounterpartyByIdAsync(Guid counterpartyId)
    {
        // Этот метод требует доступа к БД для получения FimBizContractorId
        // Используйте GetCounterpartyByFimBizIdAsync если у вас есть FimBizContractorId
        _logger.LogWarning("GetCounterpartyByIdAsync требует FimBizContractorId. Используйте GetCounterpartyByFimBizIdAsync");
        return null;
    }

    public async Task<Counterparty?> GetCounterpartyByFimBizIdAsync(int fimBizContractorId)
    {
        try
        {
            var request = new GetContractorRequest
            {
                ContractorId = fimBizContractorId
            };

            var headers = CreateHeaders();
            var contractor = await _client.GetContractorAsync(request, headers);

            return MapToCounterparty(contractor);
        }
        catch (RpcException ex)
        {
            if (ex.StatusCode == StatusCode.NotFound)
            {
                _logger.LogWarning("Контрагент с FimBiz ID {FimBizContractorId} не найден", fimBizContractorId);
                return null;
            }

            _logger.LogError(ex, "Ошибка gRPC при получении контрагента по FimBiz ID {FimBizContractorId}", fimBizContractorId);
            
            if (ex.StatusCode == StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("Неверный API ключ для FimBiz");
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении контрагента по FimBiz ID {FimBizContractorId}", fimBizContractorId);
            return null;
        }
    }

    public async Task<List<Discount>> GetCounterpartyDiscountsAsync(int fimBizContractorId)
    {
        try
        {
            var contractor = await GetCounterpartyByFimBizIdAsync(fimBizContractorId);
            if (contractor == null)
            {
                return new List<Discount>();
            }

            // Получаем контрагента с полными данными для извлечения скидок
            var request = new GetContractorRequest
            {
                ContractorId = fimBizContractorId
            };

            var headers = CreateHeaders();
            var fullContractor = await _client.GetContractorAsync(request, headers);

            // Преобразуем DiscountRules в Discount
            // Нужен counterpartyId из локальной БД, но пока используем временный GUID
            var discounts = MapDiscountRules(fullContractor.DiscountRules.ToList(), Guid.NewGuid());

            return discounts;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при получении скидок контрагента {FimBizContractorId}", fimBizContractorId);
            return new List<Discount>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении скидок контрагента {FimBizContractorId}", fimBizContractorId);
            return new List<Discount>();
        }
    }

    public async Task SyncCounterpartyAsync(Guid counterpartyId)
    {
        try
        {
            // Аналогично - нужен доступ к БД для получения FimBizContractorId
            _logger.LogWarning("SyncCounterpartyAsync требует доступа к БД");
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при синхронизации контрагента {CounterpartyId}", counterpartyId);
            throw;
        }
    }

    public async Task<GetContractorsResponse> GetContractorsAsync(GetContractorsRequest request)
    {
        try
        {
            var headers = CreateHeaders();
            return await _client.GetContractorsAsync(request, headers);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при получении списка контрагентов");
            if (ex.StatusCode == StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("Неверный API ключ для FimBiz");
            }
            throw;
        }
    }

    public AsyncServerStreamingCall<ContractorChange> SubscribeToChanges(SubscribeRequest request)
    {
        var headers = CreateHeaders();
        return _client.SubscribeToChanges(request, headers);
    }

    private Counterparty MapToCounterparty(Contractor contractor)
    {
        // Преобразование типа контрагента
        var counterpartyType = contractor.Type?.ToLower() switch
        {
            "юридическое лицо" or "юр. лицо" or "юридическое" => CounterpartyType.B2B,
            "физическое лицо" or "физ. лицо" or "физическое" => CounterpartyType.B2C,
            _ => CounterpartyType.B2C // По умолчанию
        };

        // Преобразование Unix timestamp в DateTime
        var createdAt = contractor.CreatedAt > 0 
            ? DateTimeOffset.FromUnixTimeSeconds(contractor.CreatedAt).UtcDateTime 
            : DateTime.UtcNow;
        
        var updatedAt = contractor.UpdatedAt > 0 
            ? DateTimeOffset.FromUnixTimeSeconds(contractor.UpdatedAt).UtcDateTime 
            : DateTime.UtcNow;

        return new Counterparty
        {
            Id = Guid.NewGuid(), // Генерируем новый GUID для локальной БД
            FimBizContractorId = contractor.ContractorId,
            FimBizCompanyId = contractor.CompanyId > 0 ? contractor.CompanyId : null,
            FimBizOrganizationId = contractor.OrganizationId > 0 ? contractor.OrganizationId : null,
            LastSyncVersion = contractor.SyncVersion > 0 ? contractor.SyncVersion : null,
            Name = contractor.Name ?? string.Empty,
            PhoneNumber = contractor.CorporatePhoneNumber ?? contractor.Phone ?? string.Empty,
            Type = counterpartyType,
            Email = string.IsNullOrEmpty(contractor.Email) ? null : contractor.Email,
            Inn = string.IsNullOrEmpty(contractor.Inn) ? null : contractor.Inn,
            Kpp = string.IsNullOrEmpty(contractor.Kpp) ? null : contractor.Kpp,
            LegalAddress = string.IsNullOrEmpty(contractor.Address) ? null : contractor.Address,
            EdoIdentifier = null, // Пока нет в proto
            HasPostPayment = false, // Пока нет в proto, нужно добавить
            IsCreateCabinet = contractor.IsCreateCabinet,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    private List<Discount> MapDiscountRules(List<DiscountRule> discountRules, Guid counterpartyId)
    {
        var discounts = new List<Discount>();
        var now = DateTime.UtcNow;

        foreach (var rule in discountRules.Where(r => r.IsActive))
        {
            // Проверка времени действия
            var validFrom = rule.HasValidFrom && rule.ValidFrom > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rule.ValidFrom).UtcDateTime 
                : DateTime.UtcNow;
            
            var validTo = rule.HasValidTo && rule.ValidTo > 0
                ? DateTimeOffset.FromUnixTimeSeconds(rule.ValidTo).UtcDateTime 
                : (DateTime?)null;

            if (validFrom > now || (validTo.HasValue && validTo.Value < now))
                continue;

            discounts.Add(new Discount
            {
                Id = Guid.NewGuid(),
                CounterpartyId = counterpartyId,
                NomenclatureGroupId = rule.NomenclatureGroupId > 0 
                    ? Guid.Parse(rule.NomenclatureGroupId.ToString()) 
                    : null,
                NomenclatureId = null, // Скидка на группу, не на конкретную позицию
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
            });
        }

        return discounts;
    }

    private Metadata CreateHeaders()
    {
        return new Metadata
        {
            { "x-api-key", _apiKey }
        };
    }

    private string NormalizePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return string.Empty;

        // Убираем все нецифровые символы, кроме + в начале
        var normalized = phoneNumber.Replace(" ", "")
            .Replace("-", "")
            .Replace("(", "")
            .Replace(")", "");

        // Если начинается с +7, убираем +
        if (normalized.StartsWith("+7"))
            normalized = normalized.Substring(1);

        // Если начинается с 8, заменяем на 7
        if (normalized.StartsWith("8"))
            normalized = "7" + normalized.Substring(1);

        return normalized;
    }

    public void Dispose()
    {
        _channel?.Dispose();
    }
}
