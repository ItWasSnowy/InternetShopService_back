using System.Linq;
using System.Text.Json;
using Grpc.Core;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Infrastructure.Notifications;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderStatus = InternetShopService_back.Modules.OrderManagement.Models.OrderStatus;
using GrpcOrderStatus = InternetShopService_back.Infrastructure.Grpc.Orders.OrderStatus;
using GrpcDeliveryType = InternetShopService_back.Infrastructure.Grpc.Orders.DeliveryType;
using LocalDeliveryType = InternetShopService_back.Modules.OrderManagement.Models.DeliveryType;
using LocalOrder = InternetShopService_back.Modules.OrderManagement.Models.Order;
using LocalOrderItem = InternetShopService_back.Modules.OrderManagement.Models.OrderItem;
using GrpcOrder = InternetShopService_back.Infrastructure.Grpc.Orders.Order;
using GrpcOrderItem = InternetShopService_back.Infrastructure.Grpc.Orders.OrderItem;
using GrpcAttachedFile = InternetShopService_back.Infrastructure.Grpc.Orders.AttachedFile;

namespace InternetShopService_back.Infrastructure.Grpc;

/// <summary>
/// gRPC сервис для обработки уведомлений об изменении заказов от FimBiz
/// </summary>
public class OrderSyncGrpcService : OrderSyncServerService.OrderSyncServerServiceBase
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<OrderSyncGrpcService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;
    private readonly IEmailService _emailService;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly IShopRepository _shopRepository;
    private readonly IFimBizGrpcClient _fimBizGrpcClient;
    private readonly IUserAccountRepository _userAccountRepository;

    public OrderSyncGrpcService(
        IOrderRepository orderRepository,
        ILogger<OrderSyncGrpcService> logger,
        IConfiguration configuration,
        ApplicationDbContext dbContext,
        IEmailService emailService,
        ICounterpartyRepository counterpartyRepository,
        IShopRepository shopRepository,
        IFimBizGrpcClient fimBizGrpcClient,
        IUserAccountRepository userAccountRepository)
    {
        _orderRepository = orderRepository;
        _logger = logger;
        _configuration = configuration;
        _dbContext = dbContext;
        _emailService = emailService;
        _counterpartyRepository = counterpartyRepository;
        _shopRepository = shopRepository;
        _fimBizGrpcClient = fimBizGrpcClient;
        _userAccountRepository = userAccountRepository;
    }

    /// <summary>
    /// Обработка уведомления об изменении статуса заказа от FimBiz
    /// </summary>
    public override async Task<NotifyOrderStatusChangeResponse> NotifyOrderStatusChange(
        NotifyOrderStatusChangeRequest request,
        ServerCallContext context)
    {
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ В САМОМ НАЧАЛЕ =====
        _logger.LogInformation("=== [ORDER] ВХОДЯЩИЙ ЗАПРОС NotifyOrderStatusChange ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Host: {Host}", context.RequestHeaders.GetValue("host"));
        _logger.LogInformation("User-Agent: {UserAgent}", context.RequestHeaders.GetValue("user-agent"));
        _logger.LogInformation("Content-Type: {ContentType}", context.RequestHeaders.GetValue("content-type"));
        
        var allHeaders = string.Join(", ", context.RequestHeaders.Select(h => $"{h.Key}={h.Value}"));
        _logger.LogInformation("Все заголовки: {Headers}", allHeaders);
        
        if (request != null)
        {
            _logger.LogInformation("Request.ExternalOrderId: {ExternalOrderId}", request.ExternalOrderId);
            _logger.LogInformation("Request.FimBizOrderId: {FimBizOrderId}", request.FimBizOrderId);
            _logger.LogInformation("Request.NewStatus: {NewStatus}", request.NewStatus);
            _logger.LogInformation("Request.IsPriority: {IsPriority}", request.IsPriority);
            _logger.LogInformation("Request.IsLongAssembling: {IsLongAssembling}", request.IsLongAssembling);
            _logger.LogInformation("Request.HasBillInfo: {HasBillInfo}", request.BillInfo != null);
            _logger.LogInformation("Request.HasUpdInfo: {HasUpdInfo}", request.UpdInfo != null);
        }
        else
        {
            _logger.LogWarning("Request is NULL!");
        }
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            _logger.LogInformation("API ключ из запроса: {ApiKey} (первые 10 символов)", 
                string.IsNullOrEmpty(apiKey) ? "ОТСУТСТВУЕТ" : apiKey.Substring(0, Math.Min(10, apiKey.Length)) + "...");
            _logger.LogInformation("Ожидаемый API ключ: {ExpectedApiKey} (первые 10 символов)", 
                expectedApiKey?.Substring(0, Math.Min(10, expectedApiKey.Length)) + "...");
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при обновлении статуса заказа {ExternalOrderId}. " +
                    "Получен: {ReceivedKey}, Ожидается: {ExpectedKey}", 
                    request?.ExternalOrderId, 
                    string.IsNullOrEmpty(apiKey) ? "ОТСУТСТВУЕТ" : apiKey.Substring(0, Math.Min(10, apiKey.Length)) + "...",
                    expectedApiKey?.Substring(0, Math.Min(10, expectedApiKey.Length)) + "...");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            _logger.LogInformation("Получено уведомление об изменении статуса заказа {ExternalOrderId} на {NewStatus} от FimBiz", 
                request.ExternalOrderId, request.NewStatus);

            // Парсим external_order_id - может быть Guid или FIMBIZ-{orderId}
            LocalOrder? order = null;
            Guid orderId = Guid.Empty;
            
            if (Guid.TryParse(request.ExternalOrderId, out var parsedGuid))
            {
                // Стандартный формат - Guid (заказ создан в интернет-магазине)
                orderId = parsedGuid;
                _logger.LogInformation("=== [ORDER STATUS CHANGE] Поиск заказа по ExternalOrderId (Guid): {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, NewStatus: {NewStatus} ===", 
                    request.ExternalOrderId, request.FimBizOrderId, request.NewStatus);
                order = await _orderRepository.GetByIdAsync(orderId);
                
                if (order != null)
                {
                    _logger.LogInformation("=== [ORDER STATUS CHANGE] Заказ найден по Guid. OrderId: {OrderId}, FimBizOrderId: {FimBizOrderId}, CurrentStatus: {CurrentStatus} ===", 
                        order.Id, order.FimBizOrderId?.ToString() ?? "отсутствует", order.Status);
                    
                    // Проверяем, что FimBizOrderId совпадает (если он был передан)
                    if (request.FimBizOrderId > 0 && order.FimBizOrderId.HasValue && order.FimBizOrderId.Value != request.FimBizOrderId)
                    {
                        _logger.LogWarning("=== [ORDER STATUS CHANGE] Несоответствие FimBizOrderId! Заказ найден по Guid, но FimBizOrderId не совпадает. Локальный: {LocalFimBizOrderId}, От FimBiz: {FimBizOrderId} ===", 
                            order.FimBizOrderId.Value, request.FimBizOrderId);
                        // Обновляем FimBizOrderId на значение от FimBiz
                        order.FimBizOrderId = request.FimBizOrderId;
                    }
                    else if (request.FimBizOrderId > 0 && !order.FimBizOrderId.HasValue)
                    {
                        _logger.LogInformation("=== [ORDER STATUS CHANGE] Заказ найден по Guid, но FimBizOrderId отсутствовал. Устанавливаем FimBizOrderId: {FimBizOrderId} ===", 
                            request.FimBizOrderId);
                        order.FimBizOrderId = request.FimBizOrderId;
                    }
                }
                else
                {
                    _logger.LogWarning("=== [ORDER STATUS CHANGE] Заказ не найден по Guid ExternalOrderId: {ExternalOrderId}. Попытка найти по FimBizOrderId: {FimBizOrderId} ===", 
                        request.ExternalOrderId, request.FimBizOrderId);
                    
                    // Если заказ не найден по Guid, пробуем найти по FimBizOrderId
                    if (request.FimBizOrderId > 0)
                    {
                        order = await _orderRepository.GetByFimBizOrderIdAsync(request.FimBizOrderId);
                        if (order != null)
                        {
                            orderId = order.Id;
                            _logger.LogInformation("=== [ORDER STATUS CHANGE] Заказ найден по FimBizOrderId: {FimBizOrderId}. OrderId: {OrderId}, CurrentStatus: {CurrentStatus} ===", 
                                request.FimBizOrderId, order.Id, order.Status);
                        }
                        else
                        {
                            _logger.LogWarning("=== [ORDER STATUS CHANGE] Заказ не найден ни по Guid ExternalOrderId: {ExternalOrderId}, ни по FimBizOrderId: {FimBizOrderId}. Возможно, заказ еще не был создан или был удален ===", 
                                request.ExternalOrderId, request.FimBizOrderId);
                        }
                    }
                }
            }
            else if (request.ExternalOrderId.StartsWith("FIMBIZ-", StringComparison.OrdinalIgnoreCase))
            {
                // Формат FIMBIZ-{orderId} - заказ создан в FimBiz
                // Ищем заказ по FimBizOrderId
                order = await _orderRepository.GetByFimBizOrderIdAsync(request.FimBizOrderId);
                
                if (order == null)
                {
                    // Заказ не найден - попытка создать его из данных запроса или получить через GetOrderAsync
                    try
                    {
                        // Проверяем, есть ли в запросе необходимые данные для создания заказа
                        if (request.HasContractorId && request.ContractorId > 0)
                        {
                            // Есть contractor_id - создаем заказ напрямую из данных запроса
                            _logger.LogInformation("Попытка создать заказ {ExternalOrderId} (FimBizOrderId: {FimBizOrderId}) из NotifyOrderStatusChangeRequest",
                                request.ExternalOrderId, request.FimBizOrderId);
                            
                            orderId = Guid.NewGuid();
                            var createResult = await CreateOrderFromStatusChangeRequestAsync(request, orderId);
                            if (createResult.Success)
                            {
                                order = createResult.Order!;
                                _logger.LogInformation("Заказ {OrderId} успешно создан из NotifyOrderStatusChangeRequest", orderId);
                            }
                            else
                            {
                                _logger.LogWarning("Не удалось создать заказ {ExternalOrderId} из NotifyOrderStatusChangeRequest: {Message}",
                                    request.ExternalOrderId, createResult.Message);
                            }
                        }
                        else
                        {
                            // Нет contractor_id - пытаемся получить заказ через GetOrderAsync (старый способ)
                            var companyId = _configuration.GetValue<int>("FimBiz:CompanyId", 0);
                            if (companyId > 0)
                            {
                                _logger.LogInformation("ContractorId не указан в запросе. Попытка получить заказ {ExternalOrderId} через GetOrderAsync",
                                    request.ExternalOrderId);
                                
                                var getOrderRequest = new GetOrderRequest
                                {
                                    ExternalOrderId = request.ExternalOrderId,
                                    CompanyId = companyId
                                };
                                var fullOrder = await _fimBizGrpcClient.GetOrderAsync(getOrderRequest);
                                if (fullOrder != null)
                                {
                                    // Создаем заказ из полных данных
                                    orderId = Guid.NewGuid();
                                    var createResult = await CreateOrderFromFimBizAsync(fullOrder, orderId, request.ExternalOrderId);
                                    if (createResult.Success)
                                    {
                                        order = createResult.Order!;
                                        _logger.LogInformation("Заказ {OrderId} успешно создан из FimBiz через GetOrderAsync в NotifyOrderStatusChange", orderId);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("Не удалось получить заказ {ExternalOrderId} через GetOrderAsync. Заказ будет создан при следующем уведомлении NotifyOrderUpdate",
                                        request.ExternalOrderId);
                                }
                            }
                            else
                            {
                                _logger.LogWarning("FimBiz:CompanyId не настроен и ContractorId не указан в запросе. Невозможно создать заказ {ExternalOrderId}",
                                    request.ExternalOrderId);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при попытке создать заказ {ExternalOrderId} в NotifyOrderStatusChange",
                            request.ExternalOrderId);
                    }
                }
                
                if (order != null)
                {
                    orderId = order.Id;
                    _logger.LogInformation("Найден существующий заказ из FimBiz для обновления статуса. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, LocalOrderId: {OrderId}",
                        request.ExternalOrderId, request.FimBizOrderId, orderId);
                }
            }
            else
            {
                var errorMessage = "Неверный формат ID заказа";
                _logger.LogWarning("Неверный формат external_order_id: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                    request.ExternalOrderId, errorMessage);
                return new NotifyOrderStatusChangeResponse
                {
                    Success = false,
                    Message = errorMessage
                };
            }

            // Проверяем, что заказ найден
            if (order == null)
            {
                var errorMessage = "Заказ не найден";
                _logger.LogWarning("Заказ {OrderId} не найден в локальной БД. ExternalOrderId: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                    orderId, request.ExternalOrderId, errorMessage);
                return new NotifyOrderStatusChangeResponse
                {
                    Success = false,
                    Message = errorMessage
                };
            }

            // Преобразуем статус из gRPC в локальный enum
            var newStatus = MapGrpcStatusToLocal(request.NewStatus);
            
            // Специальное логирование для статуса Cancelled
            if (request.NewStatus == GrpcOrderStatus.Cancelled || newStatus == OrderStatus.Cancelled)
            {
                _logger.LogInformation("=== [ORDER STATUS CHANGE] ПОЛУЧЕН СТАТУС ОТМЕНЫ ОТ FIMBIZ ===");
                _logger.LogInformation("ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, GrpcStatus: {GrpcStatus}, LocalStatus: {LocalStatus}", 
                    request.ExternalOrderId, request.FimBizOrderId, request.NewStatus, newStatus);
            }
            
            // Сохраняем старые значения для проверки изменений
            var oldStatus = order.Status;
            var oldTotalAmount = order.TotalAmount;
            var oldTrackingNumber = order.TrackingNumber;
            var oldOrderNumber = order.OrderNumber;
            var oldIsPriority = order.IsPriority;
            var oldIsLongAssembling = order.IsLongAssembling;
            var oldFimBizOrderId = order.FimBizOrderId;
            var oldCarrier = order.Carrier;
            var oldAssembledAt = order.AssembledAt;
            var oldShippedAt = order.ShippedAt;
            var oldDeliveredAt = order.DeliveredAt;
            
            // Обновляем статус заказа (ВСЕГДА обновляем, даже если статус не изменился)
            order.Status = newStatus;
            order.FimBizOrderId = request.FimBizOrderId;
            
            // Обновляем номер заказа, если он передан от FimBiz
            if (request.HasOrderNumber && !string.IsNullOrEmpty(request.OrderNumber))
            {
                // Проверяем, не используется ли OrderNumber другим заказом
                var existingOrderWithSameNumber = await _orderRepository.GetByOrderNumberAsync(request.OrderNumber);
                if (existingOrderWithSameNumber != null && existingOrderWithSameNumber.Id != order.Id)
                {
                    _logger.LogWarning(
                        "OrderNumber {OrderNumber} уже используется заказом {ExistingOrderId}. " +
                        "Пропускаем обновление OrderNumber для заказа {OrderId}",
                        request.OrderNumber, existingOrderWithSameNumber.Id, orderId);
                    // Не обновляем OrderNumber, если он уже используется другим заказом
                }
                else
                {
                    order.OrderNumber = request.OrderNumber;
                    _logger.LogInformation("Обновлен OrderNumber заказа {OrderId} на {OrderNumber} из NotifyOrderStatusChangeRequest", 
                        orderId, request.OrderNumber);
                }
            }
            
            order.UpdatedAt = DateTime.UtcNow;

            _logger.LogInformation("Обновление статуса заказа {OrderId} с {OldStatus} на {NewStatus} (FimBiz: {GrpcStatus})", 
                orderId, oldStatus, newStatus, request.NewStatus);
            
            // Дополнительное логирование для статуса Cancelled
            if (newStatus == OrderStatus.Cancelled)
            {
                _logger.LogInformation("=== [ORDER STATUS CHANGE] Заказ {OrderId} отменен в FimBiz. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, Comment: {Comment} ===", 
                    orderId, request.ExternalOrderId, request.FimBizOrderId, request.Comment ?? "нет комментария");
            }

            // Обновляем дополнительные поля, если они переданы
            if (request.HasModifiedPrice)
            {
                order.TotalAmount = (decimal)request.ModifiedPrice / 100; // Из копеек в рубли
            }

            // Обновляем TrackingNumber (обновляем всегда, даже если пустой, чтобы очистить старое значение)
            order.TrackingNumber = string.IsNullOrEmpty(request.TrackingNumber) ? null : request.TrackingNumber;

            // Обновляем Carrier (обновляем всегда, даже если пустой, чтобы очистить старое значение)
            order.Carrier = string.IsNullOrEmpty(request.Carrier) ? null : request.Carrier;
            if (oldCarrier != order.Carrier)
            {
                _logger.LogInformation("Обновлен Carrier заказа {OrderId} с '{OldCarrier}' на '{NewCarrier}'", 
                    orderId, oldCarrier ?? "null", order.Carrier ?? "null");
            }

            // Обновляем флаги
            order.IsPriority = request.IsPriority;
            order.IsLongAssembling = request.IsLongAssembling;

            // Обновляем даты событий (если переданы)
            if (request.HasAssembledAt && request.AssembledAt > 0)
            {
                order.AssembledAt = DateTimeOffset.FromUnixTimeSeconds(request.AssembledAt).UtcDateTime;
                if (oldAssembledAt != order.AssembledAt)
                {
                    _logger.LogInformation("Обновлен AssembledAt заказа {OrderId} на {AssembledAt}", 
                        orderId, order.AssembledAt);
                }
            }

            if (request.HasShippedAt && request.ShippedAt > 0)
            {
                order.ShippedAt = DateTimeOffset.FromUnixTimeSeconds(request.ShippedAt).UtcDateTime;
                if (oldShippedAt != order.ShippedAt)
                {
                    _logger.LogInformation("Обновлен ShippedAt заказа {OrderId} на {ShippedAt}", 
                        orderId, order.ShippedAt);
                }
            }

            if (request.HasDeliveredAt && request.DeliveredAt > 0)
            {
                order.DeliveredAt = DateTimeOffset.FromUnixTimeSeconds(request.DeliveredAt).UtcDateTime;
                if (oldDeliveredAt != order.DeliveredAt)
                {
                    _logger.LogInformation("Обновлен DeliveredAt заказа {OrderId} на {DeliveredAt}", 
                        orderId, order.DeliveredAt);
                }
            }

            // Обрабатываем bill_info (счет)
            if (request.BillInfo != null)
            {
                await ProcessBillInfoAsync(order, request.BillInfo);
            }

            // Обрабатываем upd_info (УПД)
            if (request.UpdInfo != null)
            {
                await ProcessUpdInfoAsync(order, request.UpdInfo);
            }

            // TODO: Преобразовать FimBiz assembler_id и driver_id в локальные Guid
            // Это потребует дополнительной таблицы маппинга или синхронизации сотрудников
            // if (request.HasAssemblerId && request.AssemblerId > 0)
            // {
            //     order.AssemblerId = await MapFimBizEmployeeIdToLocalGuid(request.AssemblerId);
            // }
            //
            // if (request.HasDriverId && request.DriverId > 0)
            // {
            //     order.DriverId = await MapFimBizEmployeeIdToLocalGuid(request.DriverId);
            // }

            // Проверяем, были ли реальные изменения (кроме статуса)
            bool hasOtherChanges = oldTotalAmount != order.TotalAmount
                || oldTrackingNumber != order.TrackingNumber
                || oldOrderNumber != order.OrderNumber
                || oldIsPriority != order.IsPriority
                || oldIsLongAssembling != order.IsLongAssembling
                || oldFimBizOrderId != order.FimBizOrderId
                || oldCarrier != order.Carrier
                || oldAssembledAt != order.AssembledAt
                || oldShippedAt != order.ShippedAt
                || oldDeliveredAt != order.DeliveredAt
                || request.BillInfo != null
                || request.UpdInfo != null;

            // Дедупликация уведомлений: проверяем, не было ли уже обработано такое же уведомление
            var statusChangedAt = request.StatusChangedAt > 0 
                ? DateTimeOffset.FromUnixTimeSeconds(request.StatusChangedAt).UtcDateTime 
                : DateTime.UtcNow;
            
            // Логирование для отладки конвертации времени
            _logger.LogInformation(
                "🔍 [TIME DEBUG] StatusChangedAt conversion. " +
                "UnixTimestamp: {UnixTimestamp}, " +
                "Converted UTC DateTime: {UtcDateTime}, " +
                "DateTime.Kind: {Kind}, " +
                "OrderId: {OrderId}, Status: {Status}",
                request.StatusChangedAt,
                statusChangedAt,
                statusChangedAt.Kind,
                orderId,
                newStatus);
            
            bool isDuplicate = false;
            
            // Проверяем последнюю запись в истории статусов для этого заказа
            // StatusHistory должен быть загружен через Include в GetByIdAsync/GetByFimBizOrderIdAsync
            if (order.StatusHistory != null && order.StatusHistory.Any())
            {
                var lastStatusHistory = order.StatusHistory
                    .Where(h => h.Status == newStatus)
                    .OrderByDescending(h => h.ChangedAt)
                    .FirstOrDefault();
                
                if (lastStatusHistory != null && oldStatus == newStatus)
                {
                    // Проверяем, не является ли это дубликатом по времени изменения статуса
                    // Допускаем погрешность в 5 секунд для учета возможных расхождений во времени
                    var timeDifference = Math.Abs((statusChangedAt - lastStatusHistory.ChangedAt).TotalSeconds);
                    if (timeDifference < 5)
                    {
                        isDuplicate = true;
                        _logger.LogInformation("=== [DUPLICATE NOTIFICATION] Обнаружено дублирующее уведомление для заказа {OrderId}. Статус: {Status}, StatusChangedAt: {StatusChangedAt}, Последняя запись: {LastChangedAt} ===", 
                            orderId, newStatus, statusChangedAt, lastStatusHistory.ChangedAt);
                    }
                }
            }
            
            // Добавляем запись в историю статусов только если статус изменился и это не дубликат
            if (oldStatus != newStatus && !isDuplicate)
            {
                var statusHistory = new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Status = newStatus,
                    Comment = !string.IsNullOrEmpty(request.Comment) ? request.Comment : null,
                    ChangedAt = statusChangedAt
                };
                order.StatusHistory.Add(statusHistory);
                _logger.LogInformation("Добавлена запись в историю статусов для заказа {OrderId}: {OldStatus} -> {NewStatus}", 
                    orderId, oldStatus, newStatus);
            }
            else if (oldStatus == newStatus && !isDuplicate)
            {
                _logger.LogInformation("Статус заказа {OrderId} не изменился ({Status}), но другие поля могут быть обновлены", 
                    orderId, newStatus);
            }
            else if (isDuplicate)
            {
                _logger.LogInformation("=== [DUPLICATE NOTIFICATION] Пропускаем добавление записи в историю статусов для заказа {OrderId}, так как это дубликат ===", orderId);
            }

            order.SyncedWithFimBizAt = DateTime.UtcNow;

            // Проверка статуса перед обновлением: если статус не изменился и нет других изменений, пропускаем обновление
            // Если это дубликат и нет других изменений, возвращаем успешный ответ без обновления БД
            if (isDuplicate && !hasOtherChanges && oldStatus == newStatus)
            {
                _logger.LogInformation("=== [DUPLICATE NOTIFICATION] Дублирующее уведомление для заказа {OrderId} пропущено. Статус не изменился и нет других изменений ===", orderId);
                return new NotifyOrderStatusChangeResponse
                {
                    Success = true,
                    Message = "Уведомление уже было обработано ранее (дубликат)"
                };
            }

            // Сохраняем изменения (ВСЕГДА сохраняем, даже если статус не изменился, т.к. могут быть другие изменения)
            // Обрабатываем DbUpdateConcurrencyException и InvalidOperationException с повторной попыткой
            const int maxRetries = 3;
            int retryCount = 0;
            bool updateSuccess = false;
            
            _logger.LogInformation(
                "=== [ORDER UPDATE] Начало обновления заказа {OrderId}. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, " +
                "Статус: {OldStatus} -> {NewStatus}, Попытка: {RetryCount}/{MaxRetries} ===", 
                orderId, request.ExternalOrderId, request.FimBizOrderId, oldStatus, newStatus, retryCount + 1, maxRetries);
            
            while (retryCount < maxRetries && !updateSuccess)
            {
                try
                {
                    _logger.LogDebug(
                        "=== [ORDER UPDATE] Попытка обновления заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). " +
                        "Текущий статус в памяти: {CurrentStatus}, Целевой статус: {NewStatus} ===", 
                        orderId, retryCount + 1, maxRetries, order.Status, newStatus);
                    
                    await _orderRepository.UpdateAsync(order);
                    updateSuccess = true;
                    
                    _logger.LogInformation(
                        "=== [ORDER UPDATE] Заказ {OrderId} успешно обновлен (попытка {RetryCount}/{MaxRetries}). " +
                        "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId} ===", 
                        orderId, retryCount + 1, maxRetries, request.ExternalOrderId, request.FimBizOrderId);
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
                {
                    // Обработка нарушения уникальности (код 23505)
                    retryCount++;
                    
                    // Проверяем, связано ли это с OrderNumber
                    if (pgEx.ConstraintName == "IX_Orders_OrderNumber")
                    {
                        _logger.LogWarning(ex, 
                            "=== [UNIQUE CONSTRAINT VIOLATION] Нарушение уникальности OrderNumber для заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). " +
                            "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, OrderNumber: {OrderNumber}. " +
                            "Пропускаем обновление OrderNumber и повторяем сохранение. ===", 
                            orderId, retryCount, maxRetries, request.ExternalOrderId, request.FimBizOrderId, request.OrderNumber);
                        
                        // Если OrderNumber был установлен из запроса, сбрасываем его и повторяем сохранение
                        if (request.HasOrderNumber && !string.IsNullOrEmpty(request.OrderNumber) && order.OrderNumber == request.OrderNumber)
                        {
                            // Восстанавливаем старое значение OrderNumber
                            order.OrderNumber = oldOrderNumber;
                            _logger.LogInformation(
                                "=== [UNIQUE CONSTRAINT VIOLATION] Восстановлен предыдущий OrderNumber {OldOrderNumber} для заказа {OrderId} ===",
                                oldOrderNumber, orderId);
                            
                            // Продолжаем цикл для повторной попытки сохранения без обновления OrderNumber
                            continue;
                        }
                    }
                    
                    // Если это не связано с OrderNumber или превышено количество попыток, пробрасываем исключение
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError(ex, 
                            "=== [UNIQUE CONSTRAINT VIOLATION] Не удалось обновить заказ {OrderId} после {MaxRetries} попыток из-за нарушения уникальности. " +
                            "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, Constraint: {ConstraintName} ===", 
                            orderId, maxRetries, request.ExternalOrderId, request.FimBizOrderId, pgEx.ConstraintName);
                        throw;
                    }
                    
                    // Для других нарушений уникальности продолжаем попытки
                    _logger.LogWarning(ex, 
                        "=== [UNIQUE CONSTRAINT VIOLATION] Нарушение уникальности для заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). " +
                        "Constraint: {ConstraintName}. Повторяем попытку. ===", 
                        orderId, retryCount, maxRetries, pgEx.ConstraintName);
                    continue;
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, 
                        "=== [CONCURRENCY EXCEPTION] DbUpdateConcurrencyException при обновлении заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). " +
                        "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, NewStatus: {NewStatus}. " +
                        "Перезагружаем заказ и повторяем обновление. ===", 
                        orderId, retryCount, maxRetries, request.ExternalOrderId, request.FimBizOrderId, request.NewStatus);
                    
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError(ex, 
                            "=== [CONCURRENCY EXCEPTION] Не удалось обновить заказ {OrderId} после {MaxRetries} попыток из-за DbUpdateConcurrencyException. " +
                            "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId} ===", 
                            orderId, maxRetries, request.ExternalOrderId, request.FimBizOrderId);
                        throw;
                    }
                    
                    // Проверяем существование заказа перед перезагрузкой
                    var orderExists = await _orderRepository.GetByIdAsync(orderId);
                    if (orderExists == null)
                    {
                        _logger.LogError(
                            "=== [CONCURRENCY EXCEPTION] Заказ {OrderId} не найден при перезагрузке после DbUpdateConcurrencyException. " +
                            "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}. " +
                            "Возможно, заказ был удалён другим процессом между загрузкой и обновлением. ===", 
                            orderId, request.ExternalOrderId, request.FimBizOrderId);
                        throw new InvalidOperationException($"Заказ {orderId} не найден в базе данных. Возможно, он был удалён другим процессом.");
                    }
                    
                    // Перезагружаем заказ из БД перед повторной попыткой
                    var reloadedOrder = orderExists;
                    
                    // Проверяем дедупликацию для перезагруженного заказа
                    // ВАЖНО: Сохраняем старое значение статуса ДО применения изменений
                    var reloadedOldStatus = reloadedOrder.Status;
                    bool reloadedIsDuplicate = false;
                    bool reloadedStatusChanged = reloadedOldStatus != newStatus;
                    
                    if (reloadedOrder.StatusHistory != null && reloadedOrder.StatusHistory.Any())
                    {
                        var reloadedLastStatusHistory = reloadedOrder.StatusHistory
                            .Where(h => h.Status == newStatus)
                            .OrderByDescending(h => h.ChangedAt)
                            .FirstOrDefault();
                        
                        if (reloadedLastStatusHistory != null && reloadedOldStatus == newStatus)
                        {
                            var reloadedTimeDifference = Math.Abs((statusChangedAt - reloadedLastStatusHistory.ChangedAt).TotalSeconds);
                            if (reloadedTimeDifference < 5)
                            {
                                reloadedIsDuplicate = true;
                                _logger.LogInformation("=== [DUPLICATE NOTIFICATION] При перезагрузке обнаружено дублирующее уведомление для заказа {OrderId} ===", orderId);
                            }
                        }
                    }
                    
                    // Если это дубликат и нет других изменений, возвращаем успешный ответ
                    var reloadedHasOtherChanges = reloadedOrder.TotalAmount != order.TotalAmount
                        || reloadedOrder.TrackingNumber != order.TrackingNumber
                        || reloadedOrder.IsPriority != order.IsPriority
                        || reloadedOrder.IsLongAssembling != order.IsLongAssembling
                        || reloadedOrder.FimBizOrderId != order.FimBizOrderId
                        || reloadedOrder.Carrier != order.Carrier
                        || request.BillInfo != null
                        || request.UpdInfo != null;
                    
                    // Специальная обработка для Cancelled статуса: всегда обновляем статус Cancelled, даже если он уже установлен
                    // Это важно для синхронизации - если заказ был отменен в FimBiz, мы должны обновить его у нас
                    if (reloadedIsDuplicate && !reloadedHasOtherChanges && !reloadedStatusChanged && newStatus != OrderStatus.Cancelled)
                    {
                        _logger.LogInformation("=== [DUPLICATE NOTIFICATION] Дублирующее уведомление для заказа {OrderId} пропущено после перезагрузки ===", orderId);
                        return new NotifyOrderStatusChangeResponse
                        {
                            Success = true,
                            Message = "Уведомление уже было обработано ранее (дубликат)"
                        };
                    }
                    
                    // Применяем изменения к перезагруженному заказу
                    reloadedOrder.Status = newStatus;
                    reloadedOrder.FimBizOrderId = request.FimBizOrderId;
                    reloadedOrder.UpdatedAt = DateTime.UtcNow;
                    
                    if (request.HasModifiedPrice)
                    {
                        reloadedOrder.TotalAmount = (decimal)request.ModifiedPrice / 100;
                    }
                    
                    reloadedOrder.TrackingNumber = string.IsNullOrEmpty(request.TrackingNumber) ? null : request.TrackingNumber;
                    reloadedOrder.Carrier = string.IsNullOrEmpty(request.Carrier) ? null : request.Carrier;
                    reloadedOrder.IsPriority = request.IsPriority;
                    reloadedOrder.IsLongAssembling = request.IsLongAssembling;
                    
                    if (request.HasAssembledAt && request.AssembledAt > 0)
                    {
                        reloadedOrder.AssembledAt = DateTimeOffset.FromUnixTimeSeconds(request.AssembledAt).UtcDateTime;
                    }
                    
                    if (request.HasShippedAt && request.ShippedAt > 0)
                    {
                        reloadedOrder.ShippedAt = DateTimeOffset.FromUnixTimeSeconds(request.ShippedAt).UtcDateTime;
                    }
                    
                    if (request.HasDeliveredAt && request.DeliveredAt > 0)
                    {
                        reloadedOrder.DeliveredAt = DateTimeOffset.FromUnixTimeSeconds(request.DeliveredAt).UtcDateTime;
                    }
                    
                    reloadedOrder.SyncedWithFimBizAt = DateTime.UtcNow;
                    
                    // Добавляем запись в историю статусов, если статус изменился и это не дубликат
                    // ИСПРАВЛЕНО: Используем reloadedStatusChanged (сохраненное значение ДО установки статуса) вместо проверки после установки
                    if (reloadedStatusChanged && !reloadedIsDuplicate)
                    {
                        var statusHistory = new OrderStatusHistory
                        {
                            Id = Guid.NewGuid(),
                            OrderId = reloadedOrder.Id,
                            Status = newStatus,
                            Comment = !string.IsNullOrEmpty(request.Comment) ? request.Comment : null,
                            ChangedAt = statusChangedAt
                        };
                        reloadedOrder.StatusHistory.Add(statusHistory);
                        _logger.LogInformation("Добавлена запись в историю статусов для перезагруженного заказа {OrderId}: {OldStatus} -> {NewStatus}", 
                            orderId, reloadedOldStatus, newStatus);
                    }
                    else if (reloadedIsDuplicate)
                    {
                        _logger.LogInformation("Запись в историю статусов для перезагруженного заказа {OrderId} не добавлена - дубликат уведомления", orderId);
                    }
                    
                    // Обрабатываем bill_info и upd_info, если они переданы
                    if (request.BillInfo != null)
                    {
                        await ProcessBillInfoAsync(reloadedOrder, request.BillInfo);
                    }
                    
                    if (request.UpdInfo != null)
                    {
                        await ProcessUpdInfoAsync(reloadedOrder, request.UpdInfo);
                    }
                    
                    order = reloadedOrder;
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("не найден в базе данных"))
                {
                    // Обрабатываем случай, когда заказ не найден (возможно, был удалён)
                    retryCount++;
                    _logger.LogError(ex, 
                        "=== [ORDER NOT FOUND] Заказ {OrderId} не найден в базе данных при попытке обновления (попытка {RetryCount}/{MaxRetries}). " +
                        "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, NewStatus: {NewStatus}. " +
                        "Возможно, заказ был удалён другим процессом. ===", 
                        orderId, retryCount, maxRetries, request.ExternalOrderId, request.FimBizOrderId, request.NewStatus);
                    
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError(
                            "=== [ORDER NOT FOUND] Не удалось обновить заказ {OrderId} после {MaxRetries} попыток - заказ не найден в базе данных. " +
                            "ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId} ===", 
                            orderId, maxRetries, request.ExternalOrderId, request.FimBizOrderId);
                        throw;
                    }
                    
                    // Пытаемся найти заказ по FimBizOrderId, если он был передан
                    if (request.FimBizOrderId > 0)
                    {
                        var orderByFimBizId = await _orderRepository.GetByFimBizOrderIdAsync(request.FimBizOrderId);
                        if (orderByFimBizId != null)
                        {
                            _logger.LogInformation(
                                "=== [ORDER NOT FOUND] Заказ найден по FimBizOrderId {FimBizOrderId} после ошибки. " +
                                "Продолжаем обновление с новым OrderId: {NewOrderId} ===", 
                                request.FimBizOrderId, orderByFimBizId.Id);
                            orderId = orderByFimBizId.Id;
                            order = orderByFimBizId;
                            // Применяем изменения к найденному заказу
                            order.Status = newStatus;
                            order.UpdatedAt = DateTime.UtcNow;
                            // Продолжаем цикл для повторной попытки обновления
                            continue;
                        }
                    }
                    
                    // Если заказ не найден ни по ID, ни по FimBizOrderId, выбрасываем исключение
                    throw;
                }
            }

            // Отправляем уведомление при изменении статуса на ключевые статусы
            if (oldStatus != newStatus && ShouldNotifyStatus(newStatus))
            {
                await SendOrderStatusNotificationAsync(order, newStatus);
            }

            // Специальное логирование для успешной обработки Cancelled статуса
            if (newStatus == OrderStatus.Cancelled && oldStatus != newStatus)
            {
                _logger.LogInformation("=== [ORDER STATUS CHANGE] Статус Cancelled успешно обработан для заказа {OrderId}. Старый статус: {OldStatus}, Новый статус: {NewStatus}, FimBizOrderId: {FimBizOrderId} ===", 
                    orderId, oldStatus, newStatus, order.FimBizOrderId?.ToString() ?? "не указан");
            }

            _logger.LogInformation("Заказ {OrderId} успешно обновлен. Статус: {OldStatus} -> {NewStatus}, FimBizOrderId: {FimBizOrderId}", 
                orderId, oldStatus, newStatus, order.FimBizOrderId);

            return new NotifyOrderStatusChangeResponse
            {
                Success = true,
                Message = "Статус заказа успешно обновлен"
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Специальное логирование для ошибок при обработке Cancelled статуса
            if (request != null)
            {
                var newStatus = MapGrpcStatusToLocal(request.NewStatus);
                if (newStatus == OrderStatus.Cancelled || request.NewStatus == GrpcOrderStatus.Cancelled)
                {
                    _logger.LogError(ex, "=== [ORDER STATUS CHANGE] Ошибка при обработке статуса Cancelled для заказа {ExternalOrderId} ===", 
                        request.ExternalOrderId);
                    _logger.LogError("ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, GrpcStatus: {GrpcStatus}", 
                        request.ExternalOrderId, request.FimBizOrderId, request.NewStatus);
                }
            }
            _logger.LogError(ex, "Ошибка при обработке уведомления об изменении статуса заказа {ExternalOrderId}", 
                request?.ExternalOrderId ?? "неизвестен");
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    /// <summary>
    /// Обработка уведомления об обновлении заказа от FimBiz
    /// </summary>
    public override async Task<NotifyOrderUpdateResponse> NotifyOrderUpdate(
        NotifyOrderUpdateRequest request,
        ServerCallContext context)
    {
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ В САМОМ НАЧАЛЕ =====
        _logger.LogInformation("=== [ORDER] ВХОДЯЩИЙ ЗАПРОС NotifyOrderUpdate ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Host: {Host}", context.RequestHeaders.GetValue("host"));
        _logger.LogInformation("User-Agent: {UserAgent}", context.RequestHeaders.GetValue("user-agent"));
        _logger.LogInformation("Content-Type: {ContentType}", context.RequestHeaders.GetValue("content-type"));
        
        var allHeaders = string.Join(", ", context.RequestHeaders.Select(h => $"{h.Key}={h.Value}"));
        _logger.LogInformation("Все заголовки: {Headers}", allHeaders);
        
        if (request != null)
        {
            if (request.Order != null)
            {
                _logger.LogInformation("Request.Order.ExternalOrderId: {ExternalOrderId}", request.Order.ExternalOrderId);
                _logger.LogInformation("Request.Order.OrderId (FimBiz): {OrderId}", request.Order.OrderId);
                _logger.LogInformation("Request.Order.Status: {Status}", request.Order.Status);
                _logger.LogInformation("Request.Order.DeliveryType: {DeliveryType}", request.Order.DeliveryType);
                _logger.LogInformation("Request.Order.DeliveryAddress: {DeliveryAddress}", request.Order.DeliveryAddress ?? "не указан");
                _logger.LogInformation("Request.Order.Carrier: {Carrier}", request.Order.Carrier ?? "не указан");
                _logger.LogInformation("Request.Order.IsPriority: {IsPriority}", request.Order.IsPriority);
                _logger.LogInformation("Request.Order.IsLongAssembling: {IsLongAssembling}", request.Order.IsLongAssembling);
                _logger.LogInformation("Request.Order.AssemblerId: {AssemblerId}", request.Order.HasAssemblerId ? request.Order.AssemblerId.ToString() : "не указан");
                _logger.LogInformation("Request.Order.DriverId: {DriverId}", request.Order.HasDriverId ? request.Order.DriverId.ToString() : "не указан");
                _logger.LogInformation("Request.Order.HasAssembledAt: {HasAssembledAt}", request.Order.HasAssembledAt);
                _logger.LogInformation("Request.Order.HasShippedAt: {HasShippedAt}", request.Order.HasShippedAt);
                _logger.LogInformation("Request.Order.HasDeliveredAt: {HasDeliveredAt}", request.Order.HasDeliveredAt);
                _logger.LogInformation("Request.Order.HasBillInfo: {HasBillInfo}", request.Order.BillInfo != null);
                _logger.LogInformation("Request.Order.HasUpdInfo: {HasUpdInfo}", request.Order.UpdInfo != null);
                _logger.LogInformation("Request.Order.Items.Count: {ItemsCount}", request.Order.Items?.Count ?? 0);
                _logger.LogInformation("Request.Order.AttachedFiles.Count: {AttachedFilesCount}", request.Order.AttachedFiles?.Count ?? 0);
            }
            else
            {
                _logger.LogWarning("Request.Order is NULL!");
            }
        }
        else
        {
            _logger.LogWarning("Request is NULL!");
        }
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            _logger.LogInformation("API ключ из запроса: {ApiKey} (первые 10 символов)", 
                string.IsNullOrEmpty(apiKey) ? "ОТСУТСТВУЕТ" : apiKey.Substring(0, Math.Min(10, apiKey.Length)) + "...");
            _logger.LogInformation("Ожидаемый API ключ: {ExpectedApiKey} (первые 10 символов)", 
                expectedApiKey?.Substring(0, Math.Min(10, expectedApiKey.Length)) + "...");
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при обновлении заказа {ExternalOrderId}. " +
                    "Получен: {ReceivedKey}, Ожидается: {ExpectedKey}", 
                    request?.Order?.ExternalOrderId,
                    string.IsNullOrEmpty(apiKey) ? "ОТСУТСТВУЕТ" : apiKey.Substring(0, Math.Min(10, apiKey.Length)) + "...",
                    expectedApiKey?.Substring(0, Math.Min(10, expectedApiKey.Length)) + "...");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            if (request.Order == null)
            {
                _logger.LogWarning("Получен запрос NotifyOrderUpdate без Order");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Order is required"));
            }

            _logger.LogInformation("Получено уведомление об обновлении заказа {ExternalOrderId} от FimBiz", 
                request.Order.ExternalOrderId);

            // Парсим external_order_id - может быть Guid или FIMBIZ-{orderId}
            LocalOrder? order = null;
            Guid orderId;
            bool isNewOrder = false;
            
            if (Guid.TryParse(request.Order.ExternalOrderId, out var parsedGuid))
            {
                // Стандартный формат - Guid (заказ создан в интернет-магазине)
                orderId = parsedGuid;
                order = await _orderRepository.GetByIdAsync(orderId);
            }
            else if (request.Order.ExternalOrderId.StartsWith("FIMBIZ-", StringComparison.OrdinalIgnoreCase))
            {
                // Формат FIMBIZ-{orderId} - заказ создан в FimBiz
                // Ищем заказ по FimBizOrderId (это request.Order.OrderId)
                order = await _orderRepository.GetByFimBizOrderIdAsync(request.Order.OrderId);
                
                if (order == null)
                {
                    // Заказ не найден - это первое уведомление, создаем новый
                    orderId = Guid.NewGuid();
                    isNewOrder = true;
                    _logger.LogInformation("Обнаружен новый заказ, созданный в FimBiz. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, Создан новый локальный OrderId: {OrderId}",
                        request.Order.ExternalOrderId, request.Order.OrderId, orderId);
                }
                else
                {
                    // Заказ найден - используем его для обновления
                    orderId = order.Id;
                    _logger.LogInformation("Найден существующий заказ из FimBiz для обновления. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, LocalOrderId: {OrderId}",
                        request.Order.ExternalOrderId, request.Order.OrderId, orderId);
                }
            }
            else
            {
                var errorMessage = "Неверный формат ID заказа";
                _logger.LogWarning("Неверный формат external_order_id: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                    request.Order.ExternalOrderId, errorMessage);
                return new NotifyOrderUpdateResponse
                {
                    Success = false,
                    Message = errorMessage
                };
            }

            // Если заказ не найден и это новый заказ из FimBiz - создаем его
            if (order == null && isNewOrder)
            {
                // Заказ создан в FimBiz, нужно создать его в локальной БД
                // Проверяем флаг IsCreateCabinet контрагента
                var createResult = await CreateOrderFromFimBizAsync(request.Order, orderId, request.Order.ExternalOrderId);
                if (!createResult.Success)
                {
                    _logger.LogWarning("Не удалось создать заказ из FimBiz: {Message}", createResult.Message);
                    return new NotifyOrderUpdateResponse
                    {
                        Success = false,
                        Message = createResult.Message
                    };
                }
                
                order = createResult.Order!;
                _logger.LogInformation("Заказ {OrderId} успешно создан из FimBiz для контрагента с личным кабинетом", orderId);
                
                // После создания заказа продолжаем обработку как обновление
            }
            else if (order == null)
            {
                var errorMessage = "Заказ не найден";
                _logger.LogWarning("Заказ {OrderId} не найден в локальной БД. ExternalOrderId: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                    orderId, request.Order.ExternalOrderId, errorMessage);
                return new NotifyOrderUpdateResponse
                {
                    Success = false,
                    Message = errorMessage
                };
            }

            // Сохраняем старые значения для проверки изменений
            var oldStatus = order.Status;
            var oldTotalAmount = order.TotalAmount;
            var oldTrackingNumber = order.TrackingNumber;
            var oldOrderNumber = order.OrderNumber;
            var oldFimBizOrderId = order.FimBizOrderId;
            var oldDeliveryType = order.DeliveryType;
            var oldCarrier = order.Carrier;
            var oldIsPriority = order.IsPriority;
            var oldIsLongAssembling = order.IsLongAssembling;
            var oldAssembledAt = order.AssembledAt;
            var oldShippedAt = order.ShippedAt;
            var oldDeliveredAt = order.DeliveredAt;

            // Обновляем все поля заказа из FimBiz
            order.FimBizOrderId = request.Order.OrderId;
            order.OrderNumber = request.Order.OrderNumber;
            order.Status = MapGrpcStatusToLocal(request.Order.Status);
            order.TotalAmount = (decimal)request.Order.TotalPrice / 100; // Из копеек в рубли
            
            // Обновляем DeliveryType (всегда обновляем, если приходит значение от FimBiz)
            var newDeliveryType = MapGrpcDeliveryTypeToLocal(request.Order.DeliveryType);
            if (oldDeliveryType != newDeliveryType)
            {
                _logger.LogInformation("Обновлен DeliveryType заказа {OrderId} с {OldDeliveryType} ({OldValue}) на {NewDeliveryType} ({NewValue})", 
                    orderId, oldDeliveryType, (int)oldDeliveryType, newDeliveryType, (int)newDeliveryType);
            }
            order.DeliveryType = newDeliveryType;
            
            if (request.Order.HasModifiedPrice)
            {
                order.TotalAmount = (decimal)request.Order.ModifiedPrice / 100;
            }

            // Обновляем TrackingNumber (обновляем всегда, даже если пустой, чтобы очистить старое значение)
            var oldTrackingNumberValue = order.TrackingNumber;
            order.TrackingNumber = string.IsNullOrEmpty(request.Order.TrackingNumber) ? null : request.Order.TrackingNumber;
            if (oldTrackingNumberValue != order.TrackingNumber)
            {
                _logger.LogInformation("Обновлен TrackingNumber заказа {OrderId} с '{OldTrackingNumber}' на '{NewTrackingNumber}'", 
                    orderId, oldTrackingNumberValue ?? "null", order.TrackingNumber ?? "null");
            }

            // Обновляем Carrier (обновляем всегда, даже если пустой, чтобы очистить старое значение)
            var oldCarrierValue = order.Carrier;
            order.Carrier = string.IsNullOrEmpty(request.Order.Carrier) ? null : request.Order.Carrier;
            if (oldCarrierValue != order.Carrier)
            {
                _logger.LogInformation("Обновлен Carrier заказа {OrderId} с '{OldCarrier}' на '{NewCarrier}'", 
                    orderId, oldCarrierValue ?? "null", order.Carrier ?? "null");
            }

            // Обновляем флаги
            order.IsPriority = request.Order.IsPriority;
            order.IsLongAssembling = request.Order.IsLongAssembling;

            // Обновляем AssemblerId и DriverId (если переданы)
            // TODO: Преобразовать FimBiz assembler_id и driver_id в локальные Guid
            // Это потребует дополнительной таблицы маппинга или синхронизации сотрудников
            // if (request.Order.HasAssemblerId && request.Order.AssemblerId > 0)
            // {
            //     order.AssemblerId = await MapFimBizEmployeeIdToLocalGuid(request.Order.AssemblerId);
            // }
            //
            // if (request.Order.HasDriverId && request.Order.DriverId > 0)
            // {
            //     order.DriverId = await MapFimBizEmployeeIdToLocalGuid(request.Order.DriverId);
            // }

            // Обновляем даты событий (если переданы)
            if (request.Order.HasAssembledAt && request.Order.AssembledAt > 0)
            {
                order.AssembledAt = DateTimeOffset.FromUnixTimeSeconds(request.Order.AssembledAt).UtcDateTime;
            }

            if (request.Order.HasShippedAt && request.Order.ShippedAt > 0)
            {
                order.ShippedAt = DateTimeOffset.FromUnixTimeSeconds(request.Order.ShippedAt).UtcDateTime;
            }

            if (request.Order.HasDeliveredAt && request.Order.DeliveredAt > 0)
            {
                order.DeliveredAt = DateTimeOffset.FromUnixTimeSeconds(request.Order.DeliveredAt).UtcDateTime;
            }

            // Обрабатываем bill_info (счет)
            if (request.Order.BillInfo != null)
            {
                await ProcessBillInfoAsync(order, request.Order.BillInfo);
            }

            // Обрабатываем upd_info (УПД)
            if (request.Order.UpdInfo != null)
            {
                await ProcessUpdInfoAsync(order, request.Order.UpdInfo);
            }

            // Синхронизируем позиции заказа, если они переданы
            if (request.Order.Items != null && request.Order.Items.Count > 0)
            {
                await SyncOrderItemsAsync(order, request.Order.Items);
            }

            // Обрабатываем прикрепленные файлы
            if (request.Order.AttachedFiles != null && request.Order.AttachedFiles.Count > 0)
            {
                await ProcessAttachedFilesAsync(order, request.Order.AttachedFiles);
            }

            // Отправляем уведомление при изменении статуса на ключевые статусы
            if (oldStatus != order.Status && ShouldNotifyStatus(order.Status))
            {
                await SendOrderStatusNotificationAsync(order, order.Status);
            }

            // Проверяем, были ли реальные изменения
            bool hasChanges = oldStatus != order.Status
                || oldTotalAmount != order.TotalAmount
                || oldTrackingNumber != order.TrackingNumber
                || oldOrderNumber != order.OrderNumber
                || oldFimBizOrderId != order.FimBizOrderId
                || oldDeliveryType != order.DeliveryType
                || oldCarrier != order.Carrier
                || oldIsPriority != order.IsPriority
                || oldIsLongAssembling != order.IsLongAssembling
                || oldAssembledAt != order.AssembledAt
                || oldShippedAt != order.ShippedAt
                || oldDeliveredAt != order.DeliveredAt
                || request.Order.BillInfo != null
                || request.Order.UpdInfo != null
                || (request.Order.Items != null && request.Order.Items.Count > 0)
                || (request.Order.AttachedFiles != null && request.Order.AttachedFiles.Count > 0);

            if (!hasChanges)
            {
                _logger.LogDebug("Заказ {OrderId} не изменился, пропускаем обновление", orderId);
                return new NotifyOrderUpdateResponse
                {
                    Success = true,
                    Message = "Заказ не изменился"
                };
            }

            order.SyncedWithFimBizAt = DateTime.UtcNow;
            order.UpdatedAt = DateTime.UtcNow;

            // Добавляем запись в историю статусов только если статус изменился
            if (oldStatus != order.Status)
            {
                var changedAt = request.Order.StatusChangedAt > 0 
                    ? DateTimeOffset.FromUnixTimeSeconds(request.Order.StatusChangedAt).UtcDateTime 
                    : DateTime.UtcNow;
                
                // Логирование для отладки конвертации времени
                _logger.LogInformation(
                    "🔍 [TIME DEBUG] NotifyOrderUpdate StatusChangedAt conversion. " +
                    "UnixTimestamp: {UnixTimestamp}, " +
                    "Converted UTC DateTime: {UtcDateTime}, " +
                    "DateTime.Kind: {Kind}, " +
                    "OrderId: {OrderId}, Status: {Status}",
                    request.Order.StatusChangedAt,
                    changedAt,
                    changedAt.Kind,
                    order.Id,
                    order.Status);
                
                var statusHistory = new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Status = order.Status,
                    ChangedAt = changedAt
                };
                order.StatusHistory.Add(statusHistory);
            }

            // Сохраняем изменения
            await _orderRepository.UpdateAsync(order);

            _logger.LogInformation("Заказ {OrderId} успешно обновлен", orderId);

            return new NotifyOrderUpdateResponse
            {
                Success = true,
                Message = "Заказ успешно обновлен"
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке уведомления об обновлении заказа {ExternalOrderId}", 
                request.Order?.ExternalOrderId);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    /// <summary>
    /// Создание заказа из FimBiz с проверкой флага IsCreateCabinet
    /// </summary>
    private async Task<(bool Success, LocalOrder? Order, string Message)> CreateOrderFromFimBizAsync(
        GrpcOrder grpcOrder,
        Guid orderId,
        string externalOrderId)
    {
        try
        {
            // Получаем контрагента по contractor_id из FimBiz
            var contractor = await _fimBizGrpcClient.GetCounterpartyByFimBizIdAsync(grpcOrder.ContractorId);
            if (contractor == null)
            {
                return (false, null, $"Контрагент с FimBiz ID {grpcOrder.ContractorId} не найден");
            }

            // ВАЖНО: Проверяем флаг IsCreateCabinet
            if (!contractor.IsCreateCabinet)
            {
                _logger.LogWarning("Попытка создать заказ для контрагента {ContractorId} без личного кабинета (IsCreateCabinet = false). Заказ не будет создан.",
                    grpcOrder.ContractorId);
                return (false, null, "Для данного контрагента не разрешено создание заказов в интернет-магазине");
            }

            // Находим или создаем контрагента в локальной БД
            var localCounterparty = await _counterpartyRepository.GetByFimBizIdAsync(grpcOrder.ContractorId);
            if (localCounterparty == null)
            {
                // Создаем контрагента, если его нет (данные должны быть синхронизированы, но на всякий случай)
                localCounterparty = new Counterparty
                {
                    Id = Guid.NewGuid(),
                    FimBizContractorId = grpcOrder.ContractorId,
                    Name = contractor.Name,
                    PhoneNumber = contractor.PhoneNumber,
                    Email = contractor.Email,
                    Type = contractor.Type,
                    Inn = contractor.Inn,
                    Kpp = contractor.Kpp,
                    LegalAddress = contractor.LegalAddress,
                    EdoIdentifier = contractor.EdoIdentifier,
                    HasPostPayment = contractor.HasPostPayment,
                    IsCreateCabinet = contractor.IsCreateCabinet,
                    FimBizCompanyId = contractor.FimBizCompanyId,
                    FimBizOrganizationId = contractor.FimBizOrganizationId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _counterpartyRepository.CreateAsync(localCounterparty);
                _logger.LogInformation("Создан новый контрагент {CounterpartyId} из FimBiz для заказа", localCounterparty.Id);
            }

            // Находим или создаем UserAccount для контрагента
            var userAccount = await _dbContext.UserAccounts
                .FirstOrDefaultAsync(u => u.CounterpartyId == localCounterparty.Id);

            if (userAccount == null)
            {
                // Проверяем, есть ли у контрагента FimBizCompanyId для определения магазина
                if (!localCounterparty.FimBizCompanyId.HasValue)
                {
                    return (false, null, "У контрагента не указан FimBizCompanyId. Невозможно определить магазин.");
                }

                var shop = await _shopRepository.GetByFimBizCompanyIdAsync(
                    localCounterparty.FimBizCompanyId.Value,
                    localCounterparty.FimBizOrganizationId);

                if (shop == null || !shop.IsActive)
                {
                    return (false, null, 
                        $"Интернет-магазин для компании {localCounterparty.FimBizCompanyId} не найден или неактивен.");
                }

                // Создаем UserAccount для контрагента с личным кабинетом
                userAccount = new UserAccount
                {
                    Id = Guid.NewGuid(),
                    CounterpartyId = localCounterparty.Id,
                    ShopId = shop.Id,
                    PhoneNumber = localCounterparty.PhoneNumber ?? string.Empty,
                    IsFirstLogin = true,
                    IsPasswordSet = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _userAccountRepository.CreateAsync(userAccount);
                _logger.LogInformation("Создан новый UserAccount {UserAccountId} для контрагента {CounterpartyId} при создании заказа из FimBiz",
                    userAccount.Id, localCounterparty.Id);
            }

            // Создаем заказ
            var order = new LocalOrder
            {
                Id = orderId,
                UserAccountId = userAccount.Id,
                CounterpartyId = localCounterparty.Id,
                OrderNumber = grpcOrder.OrderNumber,
                Status = MapGrpcStatusToLocal(grpcOrder.Status),
                DeliveryType = MapGrpcDeliveryTypeToLocal(grpcOrder.DeliveryType),
                TotalAmount = (decimal)grpcOrder.TotalPrice / 100, // Из копеек в рубли
                FimBizOrderId = grpcOrder.OrderId,
                Carrier = string.IsNullOrEmpty(grpcOrder.Carrier) ? null : grpcOrder.Carrier,
                TrackingNumber = string.IsNullOrEmpty(grpcOrder.TrackingNumber) ? null : grpcOrder.TrackingNumber,
                IsPriority = grpcOrder.IsPriority,
                IsLongAssembling = grpcOrder.IsLongAssembling,
                CreatedAt = grpcOrder.CreatedAt > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(grpcOrder.CreatedAt).UtcDateTime
                    : DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                SyncedWithFimBizAt = DateTime.UtcNow
            };

            // Устанавливаем даты событий, если они переданы
            if (grpcOrder.HasAssembledAt && grpcOrder.AssembledAt > 0)
            {
                order.AssembledAt = DateTimeOffset.FromUnixTimeSeconds(grpcOrder.AssembledAt).UtcDateTime;
            }
            if (grpcOrder.HasShippedAt && grpcOrder.ShippedAt > 0)
            {
                order.ShippedAt = DateTimeOffset.FromUnixTimeSeconds(grpcOrder.ShippedAt).UtcDateTime;
            }
            if (grpcOrder.HasDeliveredAt && grpcOrder.DeliveredAt > 0)
            {
                order.DeliveredAt = DateTimeOffset.FromUnixTimeSeconds(grpcOrder.DeliveredAt).UtcDateTime;
            }

            // Добавляем начальную запись в историю статусов
            var initialChangedAt = grpcOrder.StatusChangedAt > 0
                ? DateTimeOffset.FromUnixTimeSeconds(grpcOrder.StatusChangedAt).UtcDateTime
                : order.CreatedAt;
            
            // Логирование для отладки конвертации времени
            _logger.LogInformation(
                "🔍 [TIME DEBUG] CreateOrderFromFimBiz initialStatusHistory. " +
                "UnixTimestamp: {UnixTimestamp}, " +
                "Converted UTC DateTime: {UtcDateTime}, " +
                "DateTime.Kind: {Kind}, " +
                "OrderId: {OrderId}, Status: {Status}, OrderCreatedAt: {OrderCreatedAt}",
                grpcOrder.StatusChangedAt,
                initialChangedAt,
                initialChangedAt.Kind,
                order.Id,
                order.Status,
                order.CreatedAt);
            
            var initialStatusHistory = new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Status = order.Status,
                ChangedAt = initialChangedAt
            };
            order.StatusHistory.Add(initialStatusHistory);

            // Добавляем позиции заказа
            if (grpcOrder.Items != null && grpcOrder.Items.Count > 0)
            {
                foreach (var grpcItem in grpcOrder.Items)
                {
                    var orderItem = new LocalOrderItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        NomenclatureId = grpcItem.HasNomenclatureId && grpcItem.NomenclatureId > 0
                            ? grpcItem.NomenclatureId
                            : 0,
                        NomenclatureName = grpcItem.Name,
                        Quantity = grpcItem.Quantity,
                        Price = (decimal)grpcItem.Price / 100, // Из копеек в рубли
                        DiscountPercent = 0,
                        TotalAmount = (decimal)grpcItem.Price / 100 * grpcItem.Quantity,
                        UrlPhotosJson = SerializeUrlPhotos(grpcItem.PhotoUrls.ToList()),
                        CreatedAt = DateTime.UtcNow
                    };
                    order.Items.Add(orderItem);
                }
            }

            // Создаем заказ в БД
            await _orderRepository.CreateAsync(order);

            // Обрабатываем bill_info (счет), если есть
            if (grpcOrder.BillInfo != null)
            {
                await ProcessBillInfoAsync(order, grpcOrder.BillInfo);
            }

            // Обрабатываем upd_info (УПД), если есть
            if (grpcOrder.UpdInfo != null)
            {
                await ProcessUpdInfoAsync(order, grpcOrder.UpdInfo);
            }

            // Обрабатываем прикрепленные файлы
            if (grpcOrder.AttachedFiles != null && grpcOrder.AttachedFiles.Count > 0)
            {
                await ProcessAttachedFilesAsync(order, grpcOrder.AttachedFiles);
            }

            _logger.LogInformation("Заказ {OrderId} успешно создан из FimBiz. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, ContractorId: {ContractorId}",
                orderId, externalOrderId, grpcOrder.OrderId, grpcOrder.ContractorId);

            return (true, order, "Заказ успешно создан");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании заказа из FimBiz. ExternalOrderId: {ExternalOrderId}, ContractorId: {ContractorId}",
                externalOrderId, grpcOrder.ContractorId);
            return (false, null, $"Ошибка при создании заказа: {ex.Message}");
        }
    }

    /// <summary>
    /// Создание заказа из NotifyOrderStatusChangeRequest (для заказов, созданных в FimBiz)
    /// </summary>
    private async Task<(bool Success, LocalOrder? Order, string Message)> CreateOrderFromStatusChangeRequestAsync(
        NotifyOrderStatusChangeRequest request,
        Guid orderId)
    {
        try
        {
            // Проверяем наличие обязательных полей
            if (!request.HasContractorId || request.ContractorId <= 0)
            {
                return (false, null, "ContractorId не указан в запросе");
            }

            // Получаем контрагента по contractor_id из FimBiz
            var contractor = await _fimBizGrpcClient.GetCounterpartyByFimBizIdAsync(request.ContractorId);
            if (contractor == null)
            {
                return (false, null, $"Контрагент с FimBiz ID {request.ContractorId} не найден");
            }

            // ВАЖНО: Проверяем флаг IsCreateCabinet
            if (!contractor.IsCreateCabinet)
            {
                _logger.LogWarning("Попытка создать заказ для контрагента {ContractorId} без личного кабинета (IsCreateCabinet = false). Заказ не будет создан.",
                    request.ContractorId);
                return (false, null, "Для данного контрагента не разрешено создание заказов в интернет-магазине");
            }

            // Находим или создаем контрагента в локальной БД
            var localCounterparty = await _counterpartyRepository.GetByFimBizIdAsync(request.ContractorId);
            if (localCounterparty == null)
            {
                // Создаем контрагента, если его нет
                localCounterparty = new Counterparty
                {
                    Id = Guid.NewGuid(),
                    FimBizContractorId = request.ContractorId,
                    Name = contractor.Name,
                    PhoneNumber = contractor.PhoneNumber,
                    Email = contractor.Email,
                    Type = contractor.Type,
                    Inn = contractor.Inn,
                    Kpp = contractor.Kpp,
                    LegalAddress = contractor.LegalAddress,
                    EdoIdentifier = contractor.EdoIdentifier,
                    HasPostPayment = contractor.HasPostPayment,
                    IsCreateCabinet = contractor.IsCreateCabinet,
                    FimBizCompanyId = contractor.FimBizCompanyId,
                    FimBizOrganizationId = contractor.FimBizOrganizationId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _counterpartyRepository.CreateAsync(localCounterparty);
                _logger.LogInformation("Создан новый контрагент {CounterpartyId} из FimBiz для заказа", localCounterparty.Id);
            }

            // Находим или создаем UserAccount для контрагента
            var userAccount = await _dbContext.UserAccounts
                .FirstOrDefaultAsync(u => u.CounterpartyId == localCounterparty.Id);

            if (userAccount == null)
            {
                // Проверяем, есть ли у контрагента FimBizCompanyId для определения магазина
                if (!localCounterparty.FimBizCompanyId.HasValue)
                {
                    return (false, null, "У контрагента не указан FimBizCompanyId. Невозможно определить магазин.");
                }

                var shop = await _shopRepository.GetByFimBizCompanyIdAsync(
                    localCounterparty.FimBizCompanyId.Value,
                    localCounterparty.FimBizOrganizationId);

                if (shop == null || !shop.IsActive)
                {
                    return (false, null, 
                        $"Интернет-магазин для компании {localCounterparty.FimBizCompanyId} не найден или неактивен.");
                }

                // Создаем UserAccount для контрагента с личным кабинетом
                userAccount = new UserAccount
                {
                    Id = Guid.NewGuid(),
                    CounterpartyId = localCounterparty.Id,
                    ShopId = shop.Id,
                    PhoneNumber = localCounterparty.PhoneNumber ?? string.Empty,
                    IsFirstLogin = true,
                    IsPasswordSet = false,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _userAccountRepository.CreateAsync(userAccount);
                _logger.LogInformation("Создан новый UserAccount {UserAccountId} для контрагента {CounterpartyId} при создании заказа из NotifyOrderStatusChange",
                    userAccount.Id, localCounterparty.Id);
            }

            // Определяем стоимость заказа
            decimal totalAmount = 0;
            if (request.HasTotalPrice && request.TotalPrice > 0)
            {
                totalAmount = (decimal)request.TotalPrice / 100; // Из копеек в рубли
            }
            else if (request.HasModifiedPrice && request.ModifiedPrice > 0)
            {
                totalAmount = (decimal)request.ModifiedPrice / 100;
            }

            // Определяем тип доставки
            var deliveryType = request.HasDeliveryType 
                ? MapGrpcDeliveryTypeToLocal(request.DeliveryType)
                : LocalDeliveryType.Pickup; // По умолчанию самовывоз

            // Создаем заказ
            var order = new LocalOrder
            {
                Id = orderId,
                UserAccountId = userAccount.Id,
                CounterpartyId = localCounterparty.Id,
                OrderNumber = request.HasOrderNumber && !string.IsNullOrEmpty(request.OrderNumber) 
                    ? request.OrderNumber 
                    : request.FimBizOrderId.ToString(),
                Status = MapGrpcStatusToLocal(request.NewStatus),
                DeliveryType = deliveryType,
                TotalAmount = totalAmount,
                FimBizOrderId = request.FimBizOrderId,
                Carrier = request.HasCarrier && !string.IsNullOrEmpty(request.Carrier) ? request.Carrier : null,
                TrackingNumber = request.HasTrackingNumber && !string.IsNullOrEmpty(request.TrackingNumber) 
                    ? request.TrackingNumber 
                    : null,
                IsPriority = request.IsPriority,
                IsLongAssembling = request.IsLongAssembling,
                CreatedAt = request.HasCreatedAt && request.CreatedAt > 0
                    ? DateTimeOffset.FromUnixTimeSeconds(request.CreatedAt).UtcDateTime
                    : DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                SyncedWithFimBizAt = DateTime.UtcNow
            };

            // Устанавливаем даты событий, если они переданы
            if (request.HasAssembledAt && request.AssembledAt > 0)
            {
                order.AssembledAt = DateTimeOffset.FromUnixTimeSeconds(request.AssembledAt).UtcDateTime;
            }
            if (request.HasShippedAt && request.ShippedAt > 0)
            {
                order.ShippedAt = DateTimeOffset.FromUnixTimeSeconds(request.ShippedAt).UtcDateTime;
            }
            if (request.HasDeliveredAt && request.DeliveredAt > 0)
            {
                order.DeliveredAt = DateTimeOffset.FromUnixTimeSeconds(request.DeliveredAt).UtcDateTime;
            }

            // Добавляем начальную запись в историю статусов
            var initialChangedAt = request.StatusChangedAt > 0
                ? DateTimeOffset.FromUnixTimeSeconds(request.StatusChangedAt).UtcDateTime
                : order.CreatedAt;
            
            // Логирование для отладки конвертации времени
            _logger.LogInformation(
                "🔍 [TIME DEBUG] CreateOrder initialStatusHistory. " +
                "UnixTimestamp: {UnixTimestamp}, " +
                "Converted UTC DateTime: {UtcDateTime}, " +
                "DateTime.Kind: {Kind}, " +
                "OrderId: {OrderId}, Status: {Status}, OrderCreatedAt: {OrderCreatedAt}",
                request.StatusChangedAt,
                initialChangedAt,
                initialChangedAt.Kind,
                order.Id,
                order.Status,
                order.CreatedAt);
            
            var initialStatusHistory = new OrderStatusHistory
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                Status = order.Status,
                Comment = request.HasComment && !string.IsNullOrEmpty(request.Comment) ? request.Comment : null,
                ChangedAt = initialChangedAt
            };
            order.StatusHistory.Add(initialStatusHistory);

            // Добавляем позиции заказа, если они переданы
            if (request.Items != null && request.Items.Count > 0)
            {
                foreach (var grpcItem in request.Items)
                {
                    var orderItem = new LocalOrderItem
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        NomenclatureId = grpcItem.HasNomenclatureId && grpcItem.NomenclatureId > 0
                            ? grpcItem.NomenclatureId
                            : 0,
                        NomenclatureName = grpcItem.Name,
                        Quantity = grpcItem.Quantity,
                        Price = (decimal)grpcItem.Price / 100, // Из копеек в рубли
                        DiscountPercent = 0,
                        TotalAmount = (decimal)grpcItem.Price / 100 * grpcItem.Quantity,
                        UrlPhotosJson = SerializeUrlPhotos(grpcItem.PhotoUrls.ToList()),
                        CreatedAt = DateTime.UtcNow
                    };
                    order.Items.Add(orderItem);
                }
            }

            // Создаем заказ в БД
            await _orderRepository.CreateAsync(order);

            // Обрабатываем bill_info (счет), если есть
            if (request.BillInfo != null)
            {
                await ProcessBillInfoAsync(order, request.BillInfo);
            }

            // Обрабатываем upd_info (УПД), если есть
            if (request.UpdInfo != null)
            {
                await ProcessUpdInfoAsync(order, request.UpdInfo);
            }

            _logger.LogInformation("Заказ {OrderId} успешно создан из NotifyOrderStatusChangeRequest. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, ContractorId: {ContractorId}",
                orderId, request.ExternalOrderId, request.FimBizOrderId, request.ContractorId);

            return (true, order, "Заказ успешно создан");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при создании заказа из NotifyOrderStatusChangeRequest. ExternalOrderId: {ExternalOrderId}, ContractorId: {ContractorId}",
                request.ExternalOrderId, request.HasContractorId ? request.ContractorId.ToString() : "не указан");
            return (false, null, $"Ошибка при создании заказа: {ex.Message}");
        }
    }

    /// <summary>
    /// Обработка уведомления об удалении заказа от FimBiz
    /// </summary>
    public override async Task<NotifyOrderDeleteResponse> NotifyOrderDelete(
        NotifyOrderDeleteRequest request,
        ServerCallContext context)
    {
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ В САМОМ НАЧАЛЕ =====
        _logger.LogInformation("=== [ORDER] ВХОДЯЩИЙ ЗАПРОС NotifyOrderDelete ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Host: {Host}", context.RequestHeaders.GetValue("host"));
        _logger.LogInformation("User-Agent: {UserAgent}", context.RequestHeaders.GetValue("user-agent"));
        _logger.LogInformation("Content-Type: {ContentType}", context.RequestHeaders.GetValue("content-type"));
        
        var allHeaders = string.Join(", ", context.RequestHeaders.Select(h => $"{h.Key}={h.Value}"));
        _logger.LogInformation("Все заголовки: {Headers}", allHeaders);
        
        if (request != null)
        {
            _logger.LogInformation("Request.ExternalOrderId: {ExternalOrderId}", request.ExternalOrderId);
            _logger.LogInformation("Request.FimBizOrderId: {FimBizOrderId}", request.FimBizOrderId);
            _logger.LogInformation("Request.Reason: {Reason}", request.Reason ?? "не указана");
        }
        else
        {
            _logger.LogWarning("Request is NULL!");
        }
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            _logger.LogInformation("API ключ из запроса: {ApiKey} (первые 10 символов)", 
                string.IsNullOrEmpty(apiKey) ? "ОТСУТСТВУЕТ" : apiKey.Substring(0, Math.Min(10, apiKey.Length)) + "...");
            _logger.LogInformation("Ожидаемый API ключ: {ExpectedApiKey} (первые 10 символов)", 
                expectedApiKey?.Substring(0, Math.Min(10, expectedApiKey.Length)) + "...");
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при удалении заказа {ExternalOrderId}. " +
                    "Получен: {ReceivedKey}, Ожидается: {ExpectedKey}", 
                    request?.ExternalOrderId,
                    string.IsNullOrEmpty(apiKey) ? "ОТСУТСТВУЕТ" : apiKey.Substring(0, Math.Min(10, apiKey.Length)) + "...",
                    expectedApiKey?.Substring(0, Math.Min(10, expectedApiKey.Length)) + "...");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            _logger.LogInformation("Получено уведомление об удалении заказа {ExternalOrderId} от FimBiz", 
                request.ExternalOrderId);

            // Парсим external_order_id - может быть Guid или FIMBIZ-{orderId}
            LocalOrder? order = null;
            Guid orderId;
            
            if (Guid.TryParse(request.ExternalOrderId, out var parsedGuid))
            {
                // Стандартный формат - Guid (заказ создан в интернет-магазине)
                orderId = parsedGuid;
                order = await _orderRepository.GetByIdAsync(orderId);
            }
            else if (request.ExternalOrderId.StartsWith("FIMBIZ-", StringComparison.OrdinalIgnoreCase))
            {
                // Формат FIMBIZ-{orderId} - заказ создан в FimBiz
                // Ищем заказ по FimBizOrderId
                order = await _orderRepository.GetByFimBizOrderIdAsync(request.FimBizOrderId);
                
                if (order == null)
                {
                    var errorMessage = "Заказ не найден";
                    _logger.LogWarning("Заказ с FimBizOrderId {FimBizOrderId} не найден в локальной БД. ExternalOrderId: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                        request.FimBizOrderId, request.ExternalOrderId, errorMessage);
                    return new NotifyOrderDeleteResponse
                    {
                        Success = false,
                        Message = errorMessage
                    };
                }
                
                orderId = order.Id;
                _logger.LogInformation("Найден существующий заказ из FimBiz для удаления. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, LocalOrderId: {OrderId}",
                    request.ExternalOrderId, request.FimBizOrderId, orderId);
            }
            else
            {
                var errorMessage = "Неверный формат ID заказа";
                _logger.LogWarning("Неверный формат external_order_id: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                    request.ExternalOrderId, errorMessage);
                return new NotifyOrderDeleteResponse
                {
                    Success = false,
                    Message = errorMessage
                };
            }

            // Проверяем, что заказ найден
            if (order == null)
            {
                var errorMessage = "Заказ не найден";
                _logger.LogWarning("Заказ {OrderId} не найден в локальной БД. ExternalOrderId: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                    orderId, request.ExternalOrderId, errorMessage);
                return new NotifyOrderDeleteResponse
                {
                    Success = false,
                    Message = errorMessage
                };
            }

            // Удаляем заказ
            var deleted = await _orderRepository.DeleteAsync(orderId);
            if (!deleted)
            {
                var errorMessage = "Не удалось удалить заказ";
                _logger.LogWarning("Не удалось удалить заказ {OrderId}. ExternalOrderId: {ExternalOrderId}. Сообщение об ошибке: {ErrorMessage}", 
                    orderId, request.ExternalOrderId, errorMessage);
                return new NotifyOrderDeleteResponse
                {
                    Success = false,
                    Message = errorMessage
                };
            }

            _logger.LogInformation("Заказ {OrderId} успешно удален по уведомлению от FimBiz. Причина: {Reason}", 
                orderId, request.Reason ?? "не указана");

            return new NotifyOrderDeleteResponse
            {
                Success = true,
                Message = "Заказ успешно удален"
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке уведомления об удалении заказа {ExternalOrderId}", 
                request.ExternalOrderId);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }

    /// <summary>
    /// Обработка информации о счете (bill_info) - сохраняем только относительный URL PDF
    /// </summary>
    private async Task ProcessBillInfoAsync(LocalOrder order, BillInfo billInfo)
    {
        try
        {
            // Сохраняем только относительный URL - фронт сам обработает
            string? pdfUrl = billInfo.PdfUrl;

            // Обрабатываем DbUpdateConcurrencyException с повторной попыткой
            const int maxRetries = 3;
            int retryCount = 0;
            bool saveSuccess = false;
            bool isNewInvoice = false;

            while (retryCount < maxRetries && !saveSuccess)
            {
                try
                {
                    // 🔥 ИСПРАВЛЕНИЕ ДУБЛИРОВАНИЯ: Отсоединяем все отслеживаемые сущности перед проверкой
                    // Это предотвращает конфликты при параллельной обработке
                    var trackedInvoices = _dbContext.ChangeTracker.Entries<Invoice>()
                        .Where(e => e.Entity.OrderId == order.Id)
                        .ToList();
                    foreach (var entry in trackedInvoices)
                    {
                        entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    }

                    // Проверяем, существует ли уже счет для этого заказа
                    // Используем AsNoTracking для избежания конфликтов отслеживания
                    var existingInvoice = await _dbContext.Invoices
                        .AsNoTracking()
                        .FirstOrDefaultAsync(i => i.OrderId == order.Id);

                    isNewInvoice = existingInvoice == null;

                    if (existingInvoice != null)
                    {
                        // Обновляем существующий счет - загружаем его заново для обновления
                        var invoiceToUpdate = await _dbContext.Invoices
                            .FirstOrDefaultAsync(i => i.Id == existingInvoice.Id);
                        
                        if (invoiceToUpdate != null)
                        {
                            invoiceToUpdate.PdfUrl = pdfUrl;
                            invoiceToUpdate.UpdatedAt = DateTime.UtcNow;
                            order.InvoiceId = invoiceToUpdate.Id;

                            _logger.LogInformation("Обновлен счет для заказа {OrderId}. InvoiceId: {InvoiceId}, PdfUrl: {PdfUrl}", 
                                order.Id, invoiceToUpdate.Id, pdfUrl ?? "не указан");
                        }
                        else
                        {
                            // Счет был удален между проверкой и обновлением - создаем новый
                            _logger.LogWarning("Счет {InvoiceId} был удален между проверкой и обновлением. Создаем новый счет для заказа {OrderId}",
                                existingInvoice.Id, order.Id);
                            isNewInvoice = true;
                        }
                    }

                    if (isNewInvoice)
                    {
                        // Проверяем еще раз перед созданием, чтобы избежать race condition
                        var doubleCheckInvoice = await _dbContext.Invoices
                            .AsNoTracking()
                            .FirstOrDefaultAsync(i => i.OrderId == order.Id);
                        
                        if (doubleCheckInvoice != null)
                        {
                            // Счет был создан другим потоком - обновляем его
                            var invoiceToUpdate = await _dbContext.Invoices
                                .FirstOrDefaultAsync(i => i.Id == doubleCheckInvoice.Id);
                            
                            if (invoiceToUpdate != null)
                            {
                                invoiceToUpdate.PdfUrl = pdfUrl;
                                invoiceToUpdate.UpdatedAt = DateTime.UtcNow;
                                order.InvoiceId = invoiceToUpdate.Id;
                                isNewInvoice = false;

                                _logger.LogInformation("Счет для заказа {OrderId} был создан другим потоком. Обновляем его. InvoiceId: {InvoiceId}, PdfUrl: {PdfUrl}",
                                    order.Id, invoiceToUpdate.Id, pdfUrl ?? "не указан");
                            }
                        }
                        else
                        {
                            // Создаем новый счет - только с URL
                            var invoice = new Invoice
                            {
                                Id = Guid.NewGuid(),
                                OrderId = order.Id,
                                PdfUrl = pdfUrl,
                                CreatedAt = DateTime.UtcNow,
                                UpdatedAt = DateTime.UtcNow
                            };

                            await _dbContext.Invoices.AddAsync(invoice);
                            order.InvoiceId = invoice.Id;

                            _logger.LogInformation("Создан новый счет для заказа {OrderId}. InvoiceId: {InvoiceId}, PdfUrl: {PdfUrl}", 
                                order.Id, invoice.Id, pdfUrl ?? "не указан");
                        }
                    }

                    await _dbContext.SaveChangesAsync();
                    saveSuccess = true;
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException pgEx && pgEx.SqlState == "23505")
                {
                    // Обработка нарушения уникальности (код 23505)
                    retryCount++;
                    
                    // Проверяем, связано ли это с OrderNumber
                    if (pgEx.ConstraintName == "IX_Orders_OrderNumber")
                    {
                        _logger.LogWarning(ex, 
                            "Нарушение уникальности OrderNumber при сохранении счета для заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). " +
                            "Перезагружаем заказ и повторяем сохранение счета. ===", 
                            order.Id, retryCount, maxRetries);
                        
                        if (retryCount >= maxRetries)
                        {
                            _logger.LogError(ex, 
                                "Не удалось сохранить счет для заказа {OrderId} после {MaxRetries} попыток из-за нарушения уникальности OrderNumber. " +
                                "Возможно, OrderNumber был изменен другим процессом. ===", 
                                order.Id, maxRetries);
                            // Не пробрасываем исключение дальше, чтобы не прервать обработку заказа
                            // Просто логируем ошибку и продолжаем
                            return;
                        }
                        
                        // Отменяем изменения в текущем контексте
                        var changedEntries = _dbContext.ChangeTracker.Entries()
                            .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added 
                                     || e.State == Microsoft.EntityFrameworkCore.EntityState.Modified 
                                     || e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted)
                            .ToList();
                        foreach (var entry in changedEntries)
                        {
                            entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                        }
                        
                        // Перезагружаем заказ из БД, чтобы получить актуальное состояние
                        var reloadedOrder = await _orderRepository.GetByIdAsync(order.Id);
                        if (reloadedOrder != null)
                        {
                            // Обновляем InvoiceId из перезагруженного заказа, если он был установлен
                            if (reloadedOrder.InvoiceId.HasValue)
                            {
                                order.InvoiceId = reloadedOrder.InvoiceId;
                            }
                            // Продолжаем цикл для повторной попытки
                            continue;
                        }
                    }
                    
                    // Если это не связано с OrderNumber или превышено количество попыток
                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError(ex, 
                            "Не удалось сохранить счет для заказа {OrderId} после {MaxRetries} попыток из-за нарушения уникальности. " +
                            "Constraint: {ConstraintName} ===", 
                            order.Id, maxRetries, pgEx.ConstraintName);
                        // Не пробрасываем исключение дальше, чтобы не прервать обработку заказа
                        return;
                    }
                    
                    // Для других нарушений уникальности продолжаем попытки
                    _logger.LogWarning(ex, 
                        "Нарушение уникальности при сохранении счета для заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). " +
                        "Constraint: {ConstraintName}. Повторяем попытку. ===", 
                        order.Id, retryCount, maxRetries, pgEx.ConstraintName);
                    
                    // Отменяем изменения в текущем контексте
                    var changedEntriesForOther = _dbContext.ChangeTracker.Entries()
                        .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added 
                                 || e.State == Microsoft.EntityFrameworkCore.EntityState.Modified 
                                 || e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted)
                        .ToList();
                    foreach (var entry in changedEntriesForOther)
                    {
                        entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    }
                    continue;
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, 
                        "DbUpdateConcurrencyException при сохранении счета для заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). Перезагружаем счет и повторяем.", 
                        order.Id, retryCount, maxRetries);

                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError(ex, 
                            "Не удалось сохранить счет для заказа {OrderId} после {MaxRetries} попыток из-за DbUpdateConcurrencyException", 
                            order.Id, maxRetries);
                        // Не пробрасываем исключение дальше, чтобы не прервать обработку заказа
                        // Просто логируем ошибку и продолжаем
                        return;
                    }

                    // Отменяем изменения в текущем контексте
                    var changedEntries = _dbContext.ChangeTracker.Entries()
                        .Where(e => e.State == Microsoft.EntityFrameworkCore.EntityState.Added 
                                 || e.State == Microsoft.EntityFrameworkCore.EntityState.Modified 
                                 || e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted)
                        .ToList();
                    foreach (var entry in changedEntries)
                    {
                        entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                    }

                    // Перезагружаем заказ из БД, чтобы получить актуальный InvoiceId
                    var reloadedOrder = await _orderRepository.GetByIdAsync(order.Id);
                    if (reloadedOrder != null && reloadedOrder.InvoiceId.HasValue)
                    {
                        order.InvoiceId = reloadedOrder.InvoiceId;
                    }
                }
            }

            // Отправляем уведомление контрагенту о создании/обновлении счета
            // Для email формируем полный URL, если он относительный
            if (saveSuccess && (isNewInvoice || !string.IsNullOrEmpty(pdfUrl)))
            {
                string? fullPdfUrlForEmail = pdfUrl;
                if (!string.IsNullOrEmpty(pdfUrl) && !pdfUrl.StartsWith("http://") && !pdfUrl.StartsWith("https://"))
                {
                    // Относительный URL - формируем полный для email
                    var fimBizBaseUrl = _configuration["FimBiz:GrpcEndpoint"]?.Replace(":443", "").Replace("https://", "https://");
                    if (string.IsNullOrEmpty(fimBizBaseUrl))
                    {
                        fimBizBaseUrl = "https://api.fimbiz.ru";
                    }
                    fullPdfUrlForEmail = fimBizBaseUrl.TrimEnd('/') + "/" + pdfUrl.TrimStart('/');
                }
                await NotifyContractorAboutBillAsync(order.Id, order.OrderNumber, fullPdfUrlForEmail);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке bill_info для заказа {OrderId}", order.Id);
            // Не прерываем выполнение, просто логируем ошибку
        }
    }

    /// <summary>
    /// Отправка уведомления контрагенту о создании/обновлении счета
    /// </summary>
    private async Task NotifyContractorAboutBillAsync(Guid orderId, string orderNumber, string? pdfUrl)
    {
        try
        {
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Не удалось отправить уведомление о счете: заказ {OrderId} не найден", orderId);
                return;
            }

            var counterparty = await _counterpartyRepository.GetByIdAsync(order.CounterpartyId);
            if (counterparty == null || string.IsNullOrEmpty(counterparty.Email))
            {
                _logger.LogWarning("Не удалось отправить уведомление о счете для заказа {OrderId}: email контрагента не указан", orderId);
                return;
            }

            await _emailService.SendBillNotificationAsync(
                counterparty.Email,
                orderId,
                orderNumber, // Используем номер заказа вместо номера счета
                pdfUrl);

            _logger.LogInformation("Отправлено уведомление о счете на email {Email} для заказа {OrderId}", 
                counterparty.Email, orderId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке уведомления о счете для заказа {OrderId}", orderId);
            // Не прерываем выполнение при ошибке отправки уведомления
        }
    }

    /// <summary>
    /// Обработка информации об УПД (upd_info)
    /// </summary>
    private async Task ProcessUpdInfoAsync(LocalOrder order, TransferDocumentInfo updInfo)
    {
        try
        {
            // Обрабатываем DbUpdateConcurrencyException с повторной попыткой
            const int maxRetries = 3;
            int retryCount = 0;
            bool saveSuccess = false;

            while (retryCount < maxRetries && !saveSuccess)
            {
                try
                {
                    // Отменяем предыдущие изменения в контексте (если это повторная попытка)
                    if (retryCount > 0)
                    {
                        var changedEntries = _dbContext.ChangeTracker.Entries()
                            .Where(e => (e.Entity is UpdDocument || e.Entity is LocalOrder) 
                                     && (e.State == Microsoft.EntityFrameworkCore.EntityState.Added 
                                         || e.State == Microsoft.EntityFrameworkCore.EntityState.Modified 
                                         || e.State == Microsoft.EntityFrameworkCore.EntityState.Deleted))
                            .ToList();
                        foreach (var entry in changedEntries)
                        {
                            entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
                        }
                    }

                    // УПД требует наличия счета
                    if (order.InvoiceId == null)
                    {
                        _logger.LogWarning("Нельзя создать УПД для заказа {OrderId} без счета", order.Id);
                        return;
                    }

                    // Проверяем, существует ли уже УПД для этого заказа
                    var existingUpd = await _dbContext.UpdDocuments
                        .FirstOrDefaultAsync(u => u.OrderId == order.Id);

                    if (existingUpd != null)
                    {
                        // Обновляем существующий УПД
                        existingUpd.DocumentNumber = updInfo.UpdNumber;
                        existingUpd.DocumentDate = updInfo.CreatedAt > 0 
                            ? DateTimeOffset.FromUnixTimeSeconds(updInfo.CreatedAt).UtcDateTime 
                            : DateTime.UtcNow;
                        existingUpd.UpdatedAt = DateTime.UtcNow;

                        _logger.LogInformation("Обновлен УПД для заказа {OrderId}. UpdId: {UpdId}, UpdNumber: {UpdNumber}", 
                            order.Id, existingUpd.Id, updInfo.UpdNumber);
                    }
                    else
                    {
                        // Создаем новый УПД
                        var updDocument = new UpdDocument
                        {
                            Id = Guid.NewGuid(),
                            OrderId = order.Id,
                            InvoiceId = order.InvoiceId.Value,
                            CounterpartyId = order.CounterpartyId,
                            DocumentNumber = updInfo.UpdNumber,
                            DocumentDate = updInfo.CreatedAt > 0 
                                ? DateTimeOffset.FromUnixTimeSeconds(updInfo.CreatedAt).UtcDateTime 
                                : DateTime.UtcNow,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };

                        await _dbContext.UpdDocuments.AddAsync(updDocument);
                        order.UpdDocumentId = updDocument.Id;

                        _logger.LogInformation("Создан новый УПД для заказа {OrderId}. UpdId: {UpdId}, UpdNumber: {UpdNumber}", 
                            order.Id, updDocument.Id, updInfo.UpdNumber);
                    }

                    await _dbContext.SaveChangesAsync();
                    saveSuccess = true;
                }
                catch (Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException ex)
                {
                    retryCount++;
                    _logger.LogWarning(ex, 
                        "DbUpdateConcurrencyException при сохранении УПД для заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). Перезагружаем УПД и повторяем.", 
                        order.Id, retryCount, maxRetries);

                    if (retryCount >= maxRetries)
                    {
                        _logger.LogError(ex, 
                            "Не удалось сохранить УПД для заказа {OrderId} после {MaxRetries} попыток из-за DbUpdateConcurrencyException", 
                            order.Id, maxRetries);
                        // Не пробрасываем исключение дальше, чтобы не прервать обработку заказа
                        // Просто логируем ошибку и продолжаем
                        return;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке upd_info для заказа {OrderId}", order.Id);
            // Не прерываем выполнение, просто логируем ошибку
        }
    }

    /// <summary>
    /// Синхронизация позиций заказа
    /// </summary>
    private async Task SyncOrderItemsAsync(LocalOrder order, IEnumerable<GrpcOrderItem> grpcItems)
    {
        try
        {
            // 🔥 ИСПРАВЛЕНИЕ EF TRACKING: Отсоединяем все отслеживаемые OrderItems перед операцией
            // Это предотвращает конфликты "instance already being tracked"
            var trackedItems = _dbContext.ChangeTracker.Entries<LocalOrderItem>()
                .Where(e => e.Entity.OrderId == order.Id)
                .ToList();
            foreach (var entry in trackedItems)
            {
                entry.State = Microsoft.EntityFrameworkCore.EntityState.Detached;
            }

            // Загружаем существующие позиции заказа с AsNoTracking для избежания конфликтов
            var existingItems = await _dbContext.OrderItems
                .AsNoTracking()
                .Where(i => i.OrderId == order.Id)
                .ToListAsync();

            // Удаляем старые позиции (если они есть)
            if (existingItems.Any())
            {
                // Загружаем для удаления
                var itemsToDelete = await _dbContext.OrderItems
                    .Where(i => i.OrderId == order.Id)
                    .ToListAsync();
                
                _dbContext.OrderItems.RemoveRange(itemsToDelete);
                await _dbContext.SaveChangesAsync();
            }

            // Создаем новые позиции из gRPC данных
            var newItems = new List<LocalOrderItem>();
            foreach (var grpcItem in grpcItems)
            {
                var orderItem = new LocalOrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    NomenclatureId = grpcItem.HasNomenclatureId && grpcItem.NomenclatureId > 0
                        ? grpcItem.NomenclatureId
                        : 0,
                    NomenclatureName = grpcItem.Name,
                    Quantity = grpcItem.Quantity,
                    Price = (decimal)grpcItem.Price / 100, // Из копеек в рубли
                    DiscountPercent = 0,
                    TotalAmount = (decimal)grpcItem.Price / 100 * grpcItem.Quantity,
                    UrlPhotosJson = SerializeUrlPhotos(grpcItem.PhotoUrls.ToList()),
                    CreatedAt = DateTime.UtcNow
                };

                newItems.Add(orderItem);
            }

            // Добавляем все новые позиции одной операцией
            await _dbContext.OrderItems.AddRangeAsync(newItems);
            await _dbContext.SaveChangesAsync();

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
    private string? SerializeUrlPhotos(List<string>? urlPhotos)
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

    /// <summary>
    /// Обработка прикрепленных файлов из FimBiz
    /// </summary>
    private async Task ProcessAttachedFilesAsync(LocalOrder order, IEnumerable<GrpcAttachedFile> attachedFiles)
    {
        try
        {
            // Загружаем существующие файлы заказа
            await _dbContext.Entry(order).Collection(o => o.Attachments).LoadAsync();

            foreach (var file in attachedFiles)
            {
                try
                {
                    // Проверяем, есть ли уже такой файл по URL
                    var existingFile = order.Attachments
                        .FirstOrDefault(a => a.FilePath.Contains(file.Url) || file.Url.Contains(a.FilePath));

                    if (existingFile != null)
                    {
                        _logger.LogDebug("Файл {FileName} уже существует для заказа {OrderId}, пропускаем", 
                            file.FileName, order.Id);
                        continue;
                    }

                    // Загружаем файл по URL
                    var fileBytes = await DownloadFileAsync(file.Url);
                    if (fileBytes == null || fileBytes.Length == 0)
                    {
                        _logger.LogWarning("Не удалось загрузить файл {FileName} по URL {Url} для заказа {OrderId}", 
                            file.FileName, file.Url, order.Id);
                        continue;
                    }

                    // Сохраняем файл локально
                    var localPath = await SaveFileLocallyAsync(order.Id, file.FileName, fileBytes);
                    if (string.IsNullOrEmpty(localPath))
                    {
                        _logger.LogWarning("Не удалось сохранить файл {FileName} локально для заказа {OrderId}", 
                            file.FileName, order.Id);
                        continue;
                    }

                    // Создаем запись в БД
                    var attachment = new OrderAttachment
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        FileName = file.FileName,
                        FilePath = localPath,
                        ContentType = file.ContentType,
                        FileSize = fileBytes.Length,
                        IsVisibleToCustomer = true, // По умолчанию файлы от FimBiz видимы клиенту
                        CreatedAt = DateTime.UtcNow
                    };

                    await _dbContext.OrderAttachments.AddAsync(attachment);
                    order.Attachments.Add(attachment);

                    _logger.LogInformation("Файл {FileName} успешно загружен и сохранен для заказа {OrderId}", 
                        file.FileName, order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обработке файла {FileName} для заказа {OrderId}", 
                        file.FileName, order.Id);
                    // Продолжаем обработку других файлов
                }
            }

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("Обработано {Count} файлов для заказа {OrderId}", 
                attachedFiles.Count(), order.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке прикрепленных файлов для заказа {OrderId}", order.Id);
            // Не прерываем выполнение, просто логируем ошибку
        }
    }

    /// <summary>
    /// Загрузка файла по URL
    /// </summary>
    private async Task<byte[]?> DownloadFileAsync(string url)
    {
        try
        {
            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromMinutes(5); // Таймаут 5 минут для больших файлов

            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при загрузке файла по URL {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Сохранение файла локально
    /// </summary>
    private async Task<string?> SaveFileLocallyAsync(Guid orderId, string fileName, byte[] fileBytes)
    {
        try
        {
            // Получаем путь для сохранения файлов из конфигурации
            var uploadsPath = _configuration["AppSettings:UploadsPath"] 
                ?? _configuration["AppSettings:FilesPath"]
                ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "orders");

            // Создаем директорию для заказа, если её нет
            var orderDirectory = Path.Combine(uploadsPath, orderId.ToString());
            Directory.CreateDirectory(orderDirectory);

            // Генерируем уникальное имя файла (добавляем timestamp для избежания конфликтов)
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var safeFileName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var uniqueFileName = $"{safeFileName}_{timestamp}{extension}";

            var filePath = Path.Combine(orderDirectory, uniqueFileName);

            // Сохраняем файл
            await File.WriteAllBytesAsync(filePath, fileBytes);

            // Возвращаем относительный путь для хранения в БД
            var relativePath = Path.Combine("uploads", "orders", orderId.ToString(), uniqueFileName)
                .Replace('\\', '/');

            return relativePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении файла {FileName} локально", fileName);
            return null;
        }
    }

    /// <summary>
    /// Проверка, нужно ли отправлять уведомление для данного статуса
    /// </summary>
    private static bool ShouldNotifyStatus(OrderStatus status)
    {
        // Уведомляем только в ключевых статусах согласно ТЗ:
        // - когда заказ перешел на ожидание оплаты
        // - когда заказ перешел на ожидание получения
        return status == OrderStatus.AwaitingPayment ||
               status == OrderStatus.AwaitingPickup;
    }

    /// <summary>
    /// Отправка уведомления о изменении статуса заказа
    /// </summary>
    private async Task SendOrderStatusNotificationAsync(LocalOrder order, OrderStatus status)
    {
        try
        {
            // Получаем контрагента для получения email
            var counterparty = await _counterpartyRepository.GetByIdAsync(order.CounterpartyId);
            if (counterparty == null || string.IsNullOrEmpty(counterparty.Email))
            {
                _logger.LogWarning("Не удалось отправить уведомление для заказа {OrderId}: email контрагента не указан", order.Id);
                return;
            }

            var statusName = GetStatusName(status);
            await _emailService.SendOrderStatusNotificationAsync(
                counterparty.Email,
                order.Id,
                statusName);
            
            _logger.LogInformation("Отправлено уведомление на email {Email} о изменении статуса заказа {OrderId} на {Status}", 
                counterparty.Email, order.Id, statusName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке уведомления о изменении статуса заказа {OrderId}", order.Id);
            // Не прерываем выполнение при ошибке отправки уведомления
        }
    }

    /// <summary>
    /// Получение названия статуса заказа
    /// </summary>
    private static string GetStatusName(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Processing => "Обрабатывается",
            OrderStatus.AwaitingPayment => "Ожидает оплаты",
            OrderStatus.InvoiceConfirmed => "Счет подтвержден",
            OrderStatus.Manufacturing => "Изготавливается",
            OrderStatus.Assembling => "Собирается",
            OrderStatus.TransferredToCarrier => "Передан в транспортную компанию",
            OrderStatus.DeliveringByCarrier => "Доставляется транспортной компанией",
            OrderStatus.Delivering => "Доставляется",
            OrderStatus.AwaitingPickup => "Ожидает получения",
            OrderStatus.Received => "Получен",
            _ => "Неизвестный статус"
        };
    }
}


