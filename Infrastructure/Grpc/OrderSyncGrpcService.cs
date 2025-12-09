using Grpc.Core;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderStatus = InternetShopService_back.Modules.OrderManagement.Models.OrderStatus;
using GrpcOrderStatus = InternetShopService_back.Infrastructure.Grpc.Orders.OrderStatus;
using LocalOrder = InternetShopService_back.Modules.OrderManagement.Models.Order;
using LocalOrderItem = InternetShopService_back.Modules.OrderManagement.Models.OrderItem;
using GrpcOrder = InternetShopService_back.Infrastructure.Grpc.Orders.Order;
using GrpcOrderItem = InternetShopService_back.Infrastructure.Grpc.Orders.OrderItem;

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

    public OrderSyncGrpcService(
        IOrderRepository orderRepository,
        ILogger<OrderSyncGrpcService> logger,
        IConfiguration configuration,
        ApplicationDbContext dbContext)
    {
        _orderRepository = orderRepository;
        _logger = logger;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Обработка уведомления об изменении статуса заказа от FimBiz
    /// </summary>
    public override async Task<NotifyOrderStatusChangeResponse> NotifyOrderStatusChange(
        NotifyOrderStatusChangeRequest request,
        ServerCallContext context)
    {
        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при обновлении статуса заказа {ExternalOrderId}", 
                    request.ExternalOrderId);
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            _logger.LogInformation("Получено уведомление об изменении статуса заказа {ExternalOrderId} на {NewStatus} от FimBiz", 
                request.ExternalOrderId, request.NewStatus);

            // Парсим external_order_id как Guid
            if (!Guid.TryParse(request.ExternalOrderId, out var orderId))
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

            // Получаем заказ из БД
            var order = await _orderRepository.GetByIdAsync(orderId);
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
            
            // Сохраняем старые значения для проверки изменений
            var oldStatus = order.Status;
            var oldTotalAmount = order.TotalAmount;
            var oldTrackingNumber = order.TrackingNumber;
            var oldIsPriority = order.IsPriority;
            var oldIsLongAssembling = order.IsLongAssembling;
            var oldFimBizOrderId = order.FimBizOrderId;
            
            // Обновляем заказ
            order.Status = newStatus;
            order.FimBizOrderId = request.FimBizOrderId;
            order.UpdatedAt = DateTime.UtcNow;

            // Обновляем дополнительные поля, если они переданы
            if (request.HasModifiedPrice)
            {
                order.TotalAmount = (decimal)request.ModifiedPrice / 100; // Из копеек в рубли
            }

            if (!string.IsNullOrEmpty(request.TrackingNumber))
            {
                order.TrackingNumber = request.TrackingNumber;
            }

            // Обновляем флаги
            order.IsPriority = request.IsPriority;
            order.IsLongAssembling = request.IsLongAssembling;

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

            // Проверяем, были ли реальные изменения
            bool hasChanges = oldStatus != newStatus
                || oldTotalAmount != order.TotalAmount
                || oldTrackingNumber != order.TrackingNumber
                || oldIsPriority != order.IsPriority
                || oldIsLongAssembling != order.IsLongAssembling
                || oldFimBizOrderId != order.FimBizOrderId
                || request.BillInfo != null
                || request.UpdInfo != null;

            if (!hasChanges)
            {
                _logger.LogDebug("Заказ {OrderId} не изменился, пропускаем обновление", orderId);
                return new NotifyOrderStatusChangeResponse
                {
                    Success = true,
                    Message = "Заказ не изменился"
                };
            }

            // Добавляем запись в историю статусов только если статус изменился
            if (oldStatus != newStatus)
            {
                var statusHistory = new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Status = newStatus,
                    Comment = !string.IsNullOrEmpty(request.Comment) ? request.Comment : null,
                    ChangedAt = request.StatusChangedAt > 0 
                        ? DateTimeOffset.FromUnixTimeSeconds(request.StatusChangedAt).UtcDateTime 
                        : DateTime.UtcNow
                };
                order.StatusHistory.Add(statusHistory);
            }

            order.SyncedWithFimBizAt = DateTime.UtcNow;

            // Сохраняем изменения
            await _orderRepository.UpdateAsync(order);

            _logger.LogInformation("Статус заказа {OrderId} успешно обновлен с {OldStatus} на {NewStatus}", 
                orderId, oldStatus, newStatus);

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
            _logger.LogError(ex, "Ошибка при обработке уведомления об изменении статуса заказа {ExternalOrderId}", 
                request.ExternalOrderId);
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
        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при обновлении заказа {ExternalOrderId}", 
                    request.Order?.ExternalOrderId);
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            if (request.Order == null)
            {
                _logger.LogWarning("Получен запрос NotifyOrderUpdate без Order");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Order is required"));
            }

            _logger.LogInformation("Получено уведомление об обновлении заказа {ExternalOrderId} от FimBiz", 
                request.Order.ExternalOrderId);

            // Парсим external_order_id как Guid
            if (!Guid.TryParse(request.Order.ExternalOrderId, out var orderId))
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

            // Получаем заказ из БД
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
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
            var oldIsPriority = order.IsPriority;
            var oldIsLongAssembling = order.IsLongAssembling;

            // Обновляем все поля заказа из FimBiz
            order.FimBizOrderId = request.Order.OrderId;
            order.OrderNumber = request.Order.OrderNumber;
            order.Status = MapGrpcStatusToLocal(request.Order.Status);
            order.TotalAmount = (decimal)request.Order.TotalPrice / 100; // Из копеек в рубли
            
            if (request.Order.HasModifiedPrice)
            {
                order.TotalAmount = (decimal)request.Order.ModifiedPrice / 100;
            }

            if (!string.IsNullOrEmpty(request.Order.TrackingNumber))
            {
                order.TrackingNumber = request.Order.TrackingNumber;
            }

            // Обновляем флаги, если они переданы (в proto они не опциональные, но проверим)
            // В proto Order нет полей IsPriority и IsLongAssembling, поэтому оставляем как есть
            // Если нужно будет добавить - нужно обновить proto файл

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

            // Проверяем, были ли реальные изменения
            bool hasChanges = oldStatus != order.Status
                || oldTotalAmount != order.TotalAmount
                || oldTrackingNumber != order.TrackingNumber
                || oldOrderNumber != order.OrderNumber
                || oldFimBizOrderId != order.FimBizOrderId
                || request.Order.BillInfo != null
                || request.Order.UpdInfo != null
                || (request.Order.Items != null && request.Order.Items.Count > 0);

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
                var statusHistory = new OrderStatusHistory
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Status = order.Status,
                    ChangedAt = request.Order.StatusChangedAt > 0 
                        ? DateTimeOffset.FromUnixTimeSeconds(request.Order.StatusChangedAt).UtcDateTime 
                        : DateTime.UtcNow
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
    /// Обработка уведомления об удалении заказа от FimBiz
    /// </summary>
    public override async Task<NotifyOrderDeleteResponse> NotifyOrderDelete(
        NotifyOrderDeleteRequest request,
        ServerCallContext context)
    {
        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при удалении заказа {ExternalOrderId}", 
                    request.ExternalOrderId);
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            _logger.LogInformation("Получено уведомление об удалении заказа {ExternalOrderId} от FimBiz", 
                request.ExternalOrderId);

            // Парсим external_order_id как Guid
            if (!Guid.TryParse(request.ExternalOrderId, out var orderId))
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

            // Получаем заказ из БД
            var order = await _orderRepository.GetByIdAsync(orderId);
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
    /// Обработка информации о счете (bill_info)
    /// </summary>
    private async Task ProcessBillInfoAsync(LocalOrder order, BillInfo billInfo)
    {
        try
        {
            // Проверяем, существует ли уже счет для этого заказа
            var existingInvoice = await _dbContext.Invoices
                .FirstOrDefaultAsync(i => i.OrderId == order.Id);

            if (existingInvoice != null)
            {
                // Обновляем существующий счет
                existingInvoice.InvoiceNumber = billInfo.BillNumber;
                existingInvoice.IsConfirmed = billInfo.Status == BillStatus.Confirmed || billInfo.Status == BillStatus.Paid;
                existingInvoice.IsPaid = billInfo.Status == BillStatus.Paid;
                existingInvoice.UpdatedAt = DateTime.UtcNow;
                
                if (billInfo.CreatedAt > 0)
                {
                    existingInvoice.InvoiceDate = DateTimeOffset.FromUnixTimeSeconds(billInfo.CreatedAt).UtcDateTime;
                }

                _logger.LogInformation("Обновлен счет для заказа {OrderId}. InvoiceId: {InvoiceId}, BillNumber: {BillNumber}", 
                    order.Id, existingInvoice.Id, billInfo.BillNumber);
            }
            else
            {
                // Создаем новый счет
                var invoice = new Invoice
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    CounterpartyId = order.CounterpartyId,
                    InvoiceNumber = billInfo.BillNumber,
                    InvoiceDate = billInfo.CreatedAt > 0 
                        ? DateTimeOffset.FromUnixTimeSeconds(billInfo.CreatedAt).UtcDateTime 
                        : DateTime.UtcNow,
                    TotalAmount = order.TotalAmount,
                    IsConfirmed = billInfo.Status == BillStatus.Confirmed || billInfo.Status == BillStatus.Paid,
                    IsPaid = billInfo.Status == BillStatus.Paid,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _dbContext.Invoices.AddAsync(invoice);
                order.InvoiceId = invoice.Id;

                _logger.LogInformation("Создан новый счет для заказа {OrderId}. InvoiceId: {InvoiceId}, BillNumber: {BillNumber}", 
                    order.Id, invoice.Id, billInfo.BillNumber);
            }

            await _dbContext.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке bill_info для заказа {OrderId}", order.Id);
            // Не прерываем выполнение, просто логируем ошибку
        }
    }

    /// <summary>
    /// Обработка информации об УПД (upd_info)
    /// </summary>
    private async Task ProcessUpdInfoAsync(LocalOrder order, TransferDocumentInfo updInfo)
    {
        try
        {
            // Проверяем, существует ли уже УПД для этого заказа
            var existingUpd = await _dbContext.UpdDocuments
                .FirstOrDefaultAsync(u => u.OrderId == order.Id);

            // УПД требует наличия счета
            if (order.InvoiceId == null)
            {
                _logger.LogWarning("Нельзя создать УПД для заказа {OrderId} без счета", order.Id);
                return;
            }

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
            // Загружаем существующие позиции заказа
            await _dbContext.Entry(order).Collection(o => o.Items).LoadAsync();

            // Для простоты удаляем все старые позиции и создаем новые
            // В реальном приложении может потребоваться более сложная логика сравнения
            var existingItems = order.Items.ToList();
            _dbContext.OrderItems.RemoveRange(existingItems);

            // Создаем новые позиции из gRPC данных
            foreach (var grpcItem in grpcItems)
            {
                var orderItem = new LocalOrderItem
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    NomenclatureId = grpcItem.HasNomenclatureId && grpcItem.NomenclatureId > 0
                        ? ConvertInt32ToGuid(grpcItem.NomenclatureId)
                        : Guid.Empty,
                    NomenclatureName = grpcItem.Name,
                    Quantity = grpcItem.Quantity,
                    Price = (decimal)grpcItem.Price / 100, // Из копеек в рубли
                    DiscountPercent = 0,
                    TotalAmount = (decimal)grpcItem.Price / 100 * grpcItem.Quantity,
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.OrderItems.AddAsync(orderItem);
            }

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
    /// Преобразование int32 в Guid (для обратной совместимости с FimBiz ID)
    /// </summary>
    private static Guid ConvertInt32ToGuid(int value)
    {
        var bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
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
            GrpcOrderStatus.BillConfirmed => OrderStatus.InvoiceConfirmed,
            GrpcOrderStatus.Manufacturing => OrderStatus.Manufacturing,
            GrpcOrderStatus.Picking => OrderStatus.Assembling,
            GrpcOrderStatus.TransferredToTransport => OrderStatus.TransferredToCarrier,
            GrpcOrderStatus.DeliveringByTransport => OrderStatus.DeliveringByCarrier,
            GrpcOrderStatus.Delivering => OrderStatus.Delivering,
            GrpcOrderStatus.AwaitingPickup => OrderStatus.AwaitingPickup,
            GrpcOrderStatus.Completed => OrderStatus.Received,
            _ => OrderStatus.Processing // По умолчанию
        };
    }
}


