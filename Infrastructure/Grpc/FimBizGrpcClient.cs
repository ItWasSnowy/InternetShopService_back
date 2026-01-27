using System.Diagnostics;
using System.Collections.Concurrent;
using Grpc.Net.Client;
using Grpc.Core;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using InternetShopService_back.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Grpc;

public class FimBizGrpcClient : IFimBizGrpcClient, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<FimBizGrpcClient> _logger;
    private readonly IShopContext _shopContext;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IShopRepository _shopRepository;

    private static readonly ConcurrentDictionary<string, GrpcChannel> ChannelsByEndpoint = new();

    public FimBizGrpcClient(
        IConfiguration configuration,
        ILogger<FimBizGrpcClient> logger,
        IShopContext shopContext,
        IHttpContextAccessor httpContextAccessor,
        IShopRepository shopRepository)
    {
        _configuration = configuration;
        _logger = logger;
        _shopContext = shopContext;
        _httpContextAccessor = httpContextAccessor;
        _shopRepository = shopRepository;
    }

    private sealed record FimBizResolvedSettings(
        string Endpoint,
        string ApiKey,
        int CompanyId,
        int? OrganizationId);

    private async Task<FimBizResolvedSettings> ResolveSettingsAsync()
    {
        var shopId = _shopContext.ShopId;

        if (!shopId.HasValue)
        {
            var httpShopId = _httpContextAccessor.HttpContext?.Items["ShopId"];
            if (httpShopId is Guid guid)
            {
                shopId = guid;
            }
            else if (httpShopId is string str && Guid.TryParse(str, out var parsed))
            {
                shopId = parsed;
            }
        }

        if (shopId.HasValue)
        {
            var shop = await _shopRepository.GetByIdAsync(shopId.Value);
            if (shop != null)
            {
                var endpoint = shop.FimBizGrpcEndpoint;
                var apiKey = shop.FimBizApiKey;

                if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(apiKey))
                {
                    return new FimBizResolvedSettings(
                        endpoint,
                        apiKey,
                        shop.FimBizCompanyId,
                        shop.FimBizOrganizationId);
                }
            }
        }

        var fallbackEndpoint = _configuration["FimBiz:GrpcEndpoint"]
            ?? throw new InvalidOperationException("FimBiz:GrpcEndpoint не настроен");
        var fallbackApiKey = _configuration["FimBiz:ApiKey"]
            ?? throw new InvalidOperationException("FimBiz:ApiKey не настроен");
        var fallbackCompanyId = _configuration.GetValue<int>("FimBiz:CompanyId", 0);
        var fallbackOrganizationId = _configuration.GetValue<int>("FimBiz:OrganizationId", 0);

        return new FimBizResolvedSettings(
            fallbackEndpoint,
            fallbackApiKey,
            fallbackCompanyId,
            fallbackOrganizationId > 0 ? fallbackOrganizationId : null);
    }

    private static GrpcChannel GetOrCreateChannel(string endpoint)
    {
        return ChannelsByEndpoint.GetOrAdd(endpoint, ep =>
            GrpcChannel.ForAddress(ep, new GrpcChannelOptions
            {
                MaxReceiveMessageSize = 4 * 1024 * 1024,
                MaxSendMessageSize = 4 * 1024 * 1024
            }));
    }

    public async Task<Counterparty?> GetCounterpartyAsync(string phoneNumber)
    {
        try
        {
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var contractorClient = new ContractorSyncService.ContractorSyncServiceClient(channel);

            // Получаем список контрагентов с фильтром по корпоративному телефону
            var request = new GetContractorsRequest
            {
                WithCorporatePhone = true,
                BuyersOnly = true,
                PageSize = 1000 // Получаем всех с корпоративным телефоном
            };
            
            if (settings.CompanyId > 0)
            {
                request.CompanyId = settings.CompanyId;
            }
            
            if (settings.OrganizationId.HasValue && settings.OrganizationId.Value > 0)
            {
                request.OrganizationId = settings.OrganizationId.Value;
            }

            var headers = CreateHeaders(settings.ApiKey);
            var response = await contractorClient.GetContractorsAsync(request, headers);

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
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var contractorClient = new ContractorSyncService.ContractorSyncServiceClient(channel);

            var request = new GetContractorRequest
            {
                ContractorId = fimBizContractorId
            };

            var headers = CreateHeaders(settings.ApiKey);
            var contractor = await contractorClient.GetContractorAsync(request, headers);

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
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var contractorClient = new ContractorSyncService.ContractorSyncServiceClient(channel);

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

            var headers = CreateHeaders(settings.ApiKey);
            var fullContractor = await contractorClient.GetContractorAsync(request, headers);

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
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var contractorClient = new ContractorSyncService.ContractorSyncServiceClient(channel);

            var headers = CreateHeaders(settings.ApiKey);
            return await contractorClient.GetContractorsAsync(request, headers);
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

    public async Task<Contractor?> GetContractorGrpcAsync(int fimBizContractorId)
    {
        try
        {
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var contractorClient = new ContractorSyncService.ContractorSyncServiceClient(channel);

            var request = new GetContractorRequest
            {
                ContractorId = fimBizContractorId
            };

            var headers = CreateHeaders(settings.ApiKey);
            var contractor = await contractorClient.GetContractorAsync(request, headers);
            
            // Логируем количество DiscountRules согласно прото файлу FimBiz (поле 28)
            _logger.LogInformation("Получен контрагент {ContractorId} из FimBiz через GetContractor. DiscountRules.Count = {Count}", 
                fimBizContractorId, contractor?.DiscountRules?.Count ?? 0);
            
            return contractor;
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

    public AsyncServerStreamingCall<ContractorChange> SubscribeToChanges(SubscribeRequest request)
    {
        var settings = ResolveSettingsAsync().GetAwaiter().GetResult();
        var channel = GetOrCreateChannel(settings.Endpoint);
        var contractorClient = new ContractorSyncService.ContractorSyncServiceClient(channel);

        var headers = CreateHeaders(settings.ApiKey);
        return contractorClient.SubscribeToChanges(request, headers);
    }

    public async Task<GetActiveSessionsResponse> GetActiveSessionsAsync(GetActiveSessionsRequest request)
    {
        // Этот метод должен быть реализован на стороне gRPC сервера
        // FimBiz вызывает этот метод для получения списка сессий контрагента
        // Пока возвращаем пустой ответ, так как у нас нет gRPC сервера
        // Реальная реализация будет в gRPC сервере, который будет использовать FimBizSessionService
        _logger.LogWarning("GetActiveSessionsAsync вызван, но gRPC сервер не реализован. Используйте FimBizSessionService напрямую.");
        return new GetActiveSessionsResponse { Sessions = { } };
    }

    // Методы для работы с заказами

    public async Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request)
    {
        try
        {
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var orderClient = new OrderSyncService.OrderSyncServiceClient(channel);

            var headers = CreateHeaders(settings.ApiKey);
            var response = await orderClient.CreateOrderAsync(request, headers);
            
            if (!response.Success)
            {
                _logger.LogWarning("FimBiz вернул неуспешный ответ при создании заказа {ExternalOrderId}: {Message}", 
                    request.ExternalOrderId, response.Message);
            }
            
            return response;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при создании заказа {ExternalOrderId}. StatusCode: {StatusCode}, Detail: {Detail}", 
                request.ExternalOrderId, ex.StatusCode, ex.Status.Detail);
            if (ex.StatusCode == StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("Неверный API ключ для FimBiz");
            }
            throw;
        }
    }

    public async Task<UpdateOrderStatusResponse> UpdateOrderStatusAsync(UpdateOrderStatusRequest request)
    {
        try
        {
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var orderClient = new OrderSyncService.OrderSyncServiceClient(channel);

            var headers = CreateHeaders(settings.ApiKey);
            return await orderClient.UpdateOrderStatusAsync(request, headers);
        }
        catch (RpcException ex)
        {
            // Различаем типы ошибок и логируем их по-разному
            switch (ex.StatusCode)
            {
                case StatusCode.Unauthenticated:
                    _logger.LogError(ex, "=== [UPDATE ORDER STATUS] ОШИБКА АВТОРИЗАЦИИ ===");
                    _logger.LogError("Неверный API ключ при обновлении статуса заказа {ExternalOrderId}. StatusCode: {StatusCode}, Detail: {Detail}", 
                        request.ExternalOrderId, ex.StatusCode, ex.Status.Detail);
                    throw new UnauthorizedAccessException("Неверный API ключ для FimBiz", ex);
                
                case StatusCode.NotFound:
                    _logger.LogWarning("=== [UPDATE ORDER STATUS] ЗАКАЗ НЕ НАЙДЕН В FIMBIZ ===");
                    _logger.LogWarning("Заказ {ExternalOrderId} не найден в FimBiz при обновлении статуса. StatusCode: {StatusCode}, Detail: {Detail}", 
                        request.ExternalOrderId, ex.StatusCode, ex.Status.Detail);
                    throw;
                
                case StatusCode.InvalidArgument:
                    _logger.LogError("=== [UPDATE ORDER STATUS] НЕВЕРНЫЕ АРГУМЕНТЫ ===");
                    _logger.LogError("Неверные аргументы при обновлении статуса заказа {ExternalOrderId}. StatusCode: {StatusCode}, Detail: {Detail}, Request: ExternalOrderId={ExtOrderId}, CompanyId={CompanyId}, NewStatus={NewStatus}", 
                        request.ExternalOrderId, ex.StatusCode, ex.Status.Detail, request.ExternalOrderId, request.CompanyId, request.NewStatus);
                    throw;
                
                case StatusCode.Internal:
                    _logger.LogError("=== [UPDATE ORDER STATUS] ВНУТРЕННЯЯ ОШИБКА FIMBIZ ===");
                    _logger.LogError("Внутренняя ошибка FimBiz при обновлении статуса заказа {ExternalOrderId}. StatusCode: {StatusCode}, Detail: {Detail}", 
                        request.ExternalOrderId, ex.StatusCode, ex.Status.Detail);
                    throw;
                
                case StatusCode.Unavailable:
                    _logger.LogError("=== [UPDATE ORDER STATUS] FIMBIZ НЕДОСТУПЕН ===");
                    _logger.LogError("FimBiz недоступен при обновлении статуса заказа {ExternalOrderId}. StatusCode: {StatusCode}, Detail: {Detail}", 
                        request.ExternalOrderId, ex.StatusCode, ex.Status.Detail);
                    throw;
                
                default:
                    _logger.LogError("=== [UPDATE ORDER STATUS] НЕИЗВЕСТНАЯ ОШИБКА gRPC ===");
                    _logger.LogError("Неизвестная ошибка gRPC при обновлении статуса заказа {ExternalOrderId}. StatusCode: {StatusCode}, Detail: {Detail}", 
                        request.ExternalOrderId, ex.StatusCode, ex.Status.Detail);
                    throw;
            }
        }
    }

    public async Task<Order> GetOrderAsync(GetOrderRequest request)
    {
        try
        {
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var orderClient = new OrderSyncService.OrderSyncServiceClient(channel);

            var headers = CreateHeaders(settings.ApiKey);
            return await orderClient.GetOrderAsync(request, headers);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при получении заказа {ExternalOrderId}", request.ExternalOrderId);
            if (ex.StatusCode == StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("Неверный API ключ для FimBiz");
            }
            if (ex.StatusCode == StatusCode.NotFound)
            {
                return null!;
            }
            throw;
        }
    }

    public async Task<CreateCommentResponse> CreateCommentAsync(CreateCommentRequest request)
    {
        try
        {
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var orderCommentClient = new OrderCommentSyncService.OrderCommentSyncServiceClient(channel);

            var headers = CreateHeaders(settings.ApiKey);
            var response = await orderCommentClient.CreateCommentAsync(request, headers);
            
            if (!response.Success)
            {
                _logger.LogWarning("FimBiz вернул неуспешный ответ при создании комментария {CommentId}: {Message}", 
                    request.Comment.CommentId, response.Message);
            }
            
            return response;
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при создании комментария {CommentId}. StatusCode: {StatusCode}, Detail: {Detail}", 
                request.Comment.CommentId, ex.StatusCode, ex.Status.Detail);
            if (ex.StatusCode == StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("Неверный API ключ для FimBiz");
            }
            throw;
        }
    }

    public async Task<GetOrderCommentsResponse> GetOrderCommentsAsync(GetOrderCommentsRequest request)
    {
        try
        {
            var settings = await ResolveSettingsAsync();
            var channel = GetOrCreateChannel(settings.Endpoint);
            var orderCommentClient = new OrderCommentSyncService.OrderCommentSyncServiceClient(channel);

            var headers = CreateHeaders(settings.ApiKey);
            return await orderCommentClient.GetOrderCommentsAsync(request, headers);
        }
        catch (RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при получении комментариев для заказа {ExternalOrderId}", request.ExternalOrderId);
            if (ex.StatusCode == StatusCode.Unauthenticated)
            {
                throw new UnauthorizedAccessException("Неверный API ключ для FimBiz");
            }
            if (ex.StatusCode == StatusCode.NotFound)
            {
                // Возвращаем пустой список комментариев
                return new GetOrderCommentsResponse();
            }
            throw;
        }
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
            HasPostPayment = contractor.IsPostPayment,
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
                    ? rule.NomenclatureGroupId 
                    : null,
                NomenclatureId = rule.HasNomenclatureId && rule.NomenclatureId > 0 
                    ? rule.NomenclatureId 
                    : null, // null если скидка на группу, иначе ID конкретного товара
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

    private static Metadata CreateHeaders(string apiKey)
    {
        return new Metadata
        {
            { "x-api-key", apiKey }
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

    /// <summary>
    /// Преобразование int32 в Guid (для обратной совместимости с FimBiz ID)
    /// Формат Guid: "00000000-0000-0000-0000-000000000019" где 19 - это hex представление числа (25 decimal = 0x19)
    /// Используем big-endian для последних 4 байт, чтобы число было в конце строки
    /// </summary>
    private static Guid ConvertInt32ToGuid(int value)
    {
        // Создаем массив из 16 байт (размер Guid)
        var bytes = new byte[16];
        
        // Заполняем первые 12 байт нулями
        // Индексы 0-11 остаются нулями
        
        // Помещаем значение int32 в последние 4 байта (индексы 12-15)
        // Используем big-endian порядок байтов для правильного отображения в hex строке
        // Например: 25 (decimal) = 0x19 (hex) -> [00, 00, 00, 19] -> "000000000019"
        var int32Bytes = BitConverter.GetBytes(value); // little-endian: [19, 00, 00, 00] для 25
        Array.Reverse(int32Bytes); // big-endian: [00, 00, 00, 19] для 25
        Array.Copy(int32Bytes, 0, bytes, 12, 4);
        
        return new Guid(bytes);
    }

    public void Dispose()
    {
    }
}
