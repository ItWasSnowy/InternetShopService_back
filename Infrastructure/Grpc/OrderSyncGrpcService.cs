using Grpc.Core;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderStatus = InternetShopService_back.Modules.OrderManagement.Models.OrderStatus;
using GrpcOrderStatus = InternetShopService_back.Infrastructure.Grpc.Orders.OrderStatus;

namespace InternetShopService_back.Infrastructure.Grpc;

/// <summary>
/// gRPC сервис для обработки уведомлений об изменении заказов от FimBiz
/// </summary>
public class OrderSyncGrpcService : OrderSyncServerService.OrderSyncServerServiceBase
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<OrderSyncGrpcService> _logger;
    private readonly IConfiguration _configuration;

    public OrderSyncGrpcService(
        IOrderRepository orderRepository,
        ILogger<OrderSyncGrpcService> logger,
        IConfiguration configuration)
    {
        _orderRepository = orderRepository;
        _logger = logger;
        _configuration = configuration;
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
                _logger.LogWarning("Неверный формат external_order_id: {ExternalOrderId}", request.ExternalOrderId);
                return new NotifyOrderStatusChangeResponse
                {
                    Success = false,
                    Message = "Неверный формат ID заказа"
                };
            }

            // Получаем заказ из БД
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Заказ {OrderId} не найден в локальной БД", orderId);
                return new NotifyOrderStatusChangeResponse
                {
                    Success = false,
                    Message = "Заказ не найден"
                };
            }

            // Преобразуем статус из gRPC в локальный enum
            var newStatus = MapGrpcStatusToLocal(request.NewStatus);
            
            // Сохраняем старый статус для логирования
            var oldStatus = order.Status;
            
            // Обновляем заказ
            order.Status = newStatus;
            order.FimBizOrderId = request.FimBizOrderId;
            order.SyncedWithFimBizAt = DateTime.UtcNow;
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
                _logger.LogWarning("Неверный формат external_order_id: {ExternalOrderId}", 
                    request.Order.ExternalOrderId);
                return new NotifyOrderUpdateResponse
                {
                    Success = false,
                    Message = "Неверный формат ID заказа"
                };
            }

            // Получаем заказ из БД
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Заказ {OrderId} не найден в локальной БД", orderId);
                return new NotifyOrderUpdateResponse
                {
                    Success = false,
                    Message = "Заказ не найден"
                };
            }

            // Сохраняем старый статус для проверки изменений
            var oldStatus = order.Status;

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
                _logger.LogWarning("Неверный формат external_order_id: {ExternalOrderId}", request.ExternalOrderId);
                return new NotifyOrderDeleteResponse
                {
                    Success = false,
                    Message = "Неверный формат ID заказа"
                };
            }

            // Получаем заказ из БД
            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
            {
                _logger.LogWarning("Заказ {OrderId} не найден в локальной БД", orderId);
                return new NotifyOrderDeleteResponse
                {
                    Success = false,
                    Message = "Заказ не найден"
                };
            }

            // Удаляем заказ
            var deleted = await _orderRepository.DeleteAsync(orderId);
            if (!deleted)
            {
                _logger.LogWarning("Не удалось удалить заказ {OrderId}", orderId);
                return new NotifyOrderDeleteResponse
                {
                    Success = false,
                    Message = "Не удалось удалить заказ"
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


