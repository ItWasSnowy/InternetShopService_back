using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Infrastructure.Notifications;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderStatus = InternetShopService_back.Modules.OrderManagement.Models.OrderStatus;
using GrpcOrder = InternetShopService_back.Infrastructure.Grpc.Orders.Order;
using GrpcOrderStatus = InternetShopService_back.Infrastructure.Grpc.Orders.OrderStatus;
using GrpcDeliveryType = InternetShopService_back.Infrastructure.Grpc.Orders.DeliveryType;
using GrpcOrderItem = InternetShopService_back.Infrastructure.Grpc.Orders.OrderItem;
using LocalOrder = InternetShopService_back.Modules.OrderManagement.Models.Order;
using LocalOrderItem = InternetShopService_back.Modules.OrderManagement.Models.OrderItem;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly IDeliveryAddressRepository _deliveryAddressRepository;
    private readonly ICargoReceiverRepository _cargoReceiverRepository;
    private readonly IShopRepository _shopRepository;
    private readonly IFimBizGrpcClient _fimBizGrpcClient;
    private readonly IEmailService _emailService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IUserAccountRepository userAccountRepository,
        ICounterpartyRepository counterpartyRepository,
        IDeliveryAddressRepository deliveryAddressRepository,
        ICargoReceiverRepository cargoReceiverRepository,
        IShopRepository shopRepository,
        IFimBizGrpcClient fimBizGrpcClient,
        IEmailService emailService,
        ApplicationDbContext context,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _userAccountRepository = userAccountRepository;
        _counterpartyRepository = counterpartyRepository;
        _deliveryAddressRepository = deliveryAddressRepository;
        _cargoReceiverRepository = cargoReceiverRepository;
        _shopRepository = shopRepository;
        _fimBizGrpcClient = fimBizGrpcClient;
        _emailService = emailService;
        _context = context;
        _logger = logger;
    }

    public async Task<OrderDto> CreateOrderAsync(CreateOrderDto dto)
    {
        // TODO: Получить userId из контекста или параметра
        // Пока используем заглушку - нужно будет передавать userId из контроллера
        throw new NotImplementedException("Используйте CreateOrderFromCartAsync для создания заказа из корзины");
    }

    public async Task<OrderDto> CreateOrderFromCartAsync(
        Guid userId,
        CreateOrderFromCartDto dto,
        List<CreateOrderItemDto> items)
    {
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
            throw new InvalidOperationException("Пользователь не найден");

        // Проверяем адрес доставки, если указан
        if (dto.DeliveryAddressId.HasValue)
        {
            var address = await _deliveryAddressRepository.GetByIdAsync(dto.DeliveryAddressId.Value);
            if (address == null || address.UserAccountId != userId)
                throw new InvalidOperationException("Адрес доставки не найден");
        }

        // Проверяем грузополучателя, если указан
        if (dto.CargoReceiverId.HasValue)
        {
            var receiver = await _cargoReceiverRepository.GetByIdAsync(dto.CargoReceiverId.Value);
            if (receiver == null || receiver.UserAccountId != userId)
                throw new InvalidOperationException("Грузополучатель не найден");
        }

        // Получаем скидки контрагента
        var discounts = await _counterpartyRepository.GetActiveDiscountsAsync(userAccount.CounterpartyId);

        // Создаем заказ
        var order = new LocalOrder
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            CounterpartyId = userAccount.CounterpartyId,
            Status = OrderStatus.Processing,
            DeliveryType = dto.DeliveryType,
            DeliveryAddressId = dto.DeliveryAddressId,
            CargoReceiverId = dto.CargoReceiverId,
            Carrier = dto.Carrier,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Добавляем позиции заказа
        decimal totalAmount = 0;
        foreach (var itemDto in items)
        {
            var discount = FindDiscountForItem(itemDto.NomenclatureId, discounts);
            var discountPercent = discount?.DiscountPercent ?? 0;
            var priceWithDiscount = itemDto.Price * (1 - discountPercent / 100);
            var itemTotalAmount = priceWithDiscount * itemDto.Quantity;

            var orderItem = new LocalOrderItem
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                NomenclatureId = itemDto.NomenclatureId,
                NomenclatureName = itemDto.NomenclatureName,
                Quantity = itemDto.Quantity,
                Price = itemDto.Price,
                DiscountPercent = discountPercent,
                TotalAmount = itemTotalAmount,
                CreatedAt = DateTime.UtcNow
            };

            order.Items.Add(orderItem);
            totalAmount += itemTotalAmount;
        }

        order.TotalAmount = totalAmount;

        // Добавляем запись в историю статусов
        var statusHistory = new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = OrderStatus.Processing,
            ChangedAt = DateTime.UtcNow
        };
        order.StatusHistory.Add(statusHistory);

        order = await _orderRepository.CreateAsync(order);

        _logger.LogInformation("Создан заказ {OrderId} для пользователя {UserId}", order.Id, userId);

        // Отправляем заказ в FimBiz
        try
        {
            await SendOrderToFimBizAsync(order, userAccount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке заказа {OrderId} в FimBiz. Заказ сохранен локально, но не синхронизирован", order.Id);
            // Не прерываем выполнение, заказ уже сохранен локально
        }

        return await MapToOrderDtoAsync(order);
    }

    public async Task<OrderDto> GetOrderAsync(Guid orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            return null!;

        return await MapToOrderDtoAsync(order);
    }

    public async Task<List<OrderDto>> GetOrdersByUserAsync(Guid userId)
    {
        var orders = await _orderRepository.GetByUserIdAsync(userId);
        var orderDtos = new List<OrderDto>();

        foreach (var order in orders)
        {
            orderDtos.Add(await MapToOrderDtoAsync(order));
        }

        return orderDtos;
    }

    public async Task<OrderDto> UpdateOrderStatusAsync(Guid orderId, OrderStatus status)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            throw new InvalidOperationException("Заказ не найден");

        var oldStatus = order.Status;
        order.Status = status;
        order.UpdatedAt = DateTime.UtcNow;

        // Добавляем запись в историю статусов
        var statusHistory = new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = status,
            ChangedAt = DateTime.UtcNow
        };
        order.StatusHistory.Add(statusHistory);

        order = await _orderRepository.UpdateAsync(order);

        _logger.LogInformation("Статус заказа {OrderId} изменен с {OldStatus} на {NewStatus}", 
            orderId, oldStatus, status);

        // Если заказ синхронизирован с FimBiz, отправляем обновление статуса
        if (order.FimBizOrderId.HasValue)
        {
            await SendOrderStatusUpdateToFimBizAsync(order, status);
        }

        // Отправляем уведомление на email контрагента при изменении статуса
        await SendOrderStatusNotificationAsync(order);

        return await MapToOrderDtoAsync(order);
    }

    private async Task SendOrderStatusNotificationAsync(LocalOrder order)
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

            // Отправляем уведомление только для ключевых статусов
            if (ShouldNotifyStatus(order.Status))
            {
                var statusName = GetStatusName(order.Status);
                await _emailService.SendOrderStatusNotificationAsync(
                    counterparty.Email,
                    order.Id,
                    statusName);
                
                _logger.LogInformation("Отправлено уведомление на email {Email} о изменении статуса заказа {OrderId}", 
                    counterparty.Email, order.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке уведомления о изменении статуса заказа {OrderId}", order.Id);
            // Не прерываем выполнение при ошибке отправки уведомления
        }
    }

    private static bool ShouldNotifyStatus(OrderStatus status)
    {
        // Уведомляем в ключевых статусах согласно ТЗ
        return status == OrderStatus.AwaitingPayment ||
               status == OrderStatus.AwaitingPickup ||
               status == OrderStatus.Received ||
               status == OrderStatus.InvoiceConfirmed;
    }

    private async Task<OrderDto> MapToOrderDtoAsync(LocalOrder order)
    {
        var dto = new OrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            StatusName = GetStatusName(order.Status),
            DeliveryType = order.DeliveryType,
            TrackingNumber = order.TrackingNumber,
            Carrier = order.Carrier,
            TotalAmount = order.TotalAmount,
            CreatedAt = order.CreatedAt,
            Items = order.Items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                NomenclatureId = i.NomenclatureId,
                NomenclatureName = i.NomenclatureName,
                Quantity = i.Quantity,
                Price = i.Price,
                DiscountPercent = i.DiscountPercent,
                TotalAmount = i.TotalAmount
            }).ToList(),
            Attachments = order.Attachments.Select(a => new OrderAttachmentDto
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                IsVisibleToCustomer = a.IsVisibleToCustomer,
                CreatedAt = a.CreatedAt
            }).ToList()
        };

        // Загружаем адрес доставки, если есть
        if (order.DeliveryAddressId.HasValue)
        {
            var address = await _deliveryAddressRepository.GetByIdAsync(order.DeliveryAddressId.Value);
            if (address != null)
            {
                dto.DeliveryAddress = new OrderManagement.DTOs.DeliveryAddressDto
                {
                    Id = address.Id,
                    Address = address.Address,
                    City = address.City,
                    Region = address.Region,
                    PostalCode = address.PostalCode
                };
            }
        }

        // Загружаем грузополучателя, если есть
        if (order.CargoReceiverId.HasValue)
        {
            var receiver = await _cargoReceiverRepository.GetByIdAsync(order.CargoReceiverId.Value);
            if (receiver != null)
            {
                dto.CargoReceiver = new OrderManagement.DTOs.CargoReceiverDto
                {
                    Id = receiver.Id,
                    FullName = receiver.FullName,
                    PassportSeries = receiver.PassportSeries,
                    PassportNumber = receiver.PassportNumber
                };
            }
        }

        return dto;
    }

    private static string GetStatusName(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Processing => "Обрабатывается",
            OrderStatus.AwaitingPayment => "Ожидает оплаты",
            OrderStatus.InvoiceConfirmed => "Счет подтвержден",
            OrderStatus.Manufacturing => "Изготавливается",
            OrderStatus.Assembling => "Собирается",
            OrderStatus.TransferredToCarrier => "Передается в транспортную компанию",
            OrderStatus.DeliveringByCarrier => "Доставляется транспортной компанией",
            OrderStatus.Delivering => "Доставляется",
            OrderStatus.AwaitingPickup => "Ожидает получения",
            OrderStatus.Received => "Получен",
            _ => "Неизвестный статус"
        };
    }

    private static Discount? FindDiscountForItem(Guid nomenclatureId, List<Discount> discounts)
    {
        // Сначала ищем скидку на конкретную позицию
        var itemDiscount = discounts.FirstOrDefault(d => 
            d.NomenclatureId == nomenclatureId && d.NomenclatureGroupId == null);
        
        if (itemDiscount != null)
        {
            return itemDiscount;
        }

        // TODO: Получить группу номенклатуры и найти скидку на группу
        return null;
    }

    private async Task SendOrderToFimBizAsync(LocalOrder order, InternetShopService_back.Modules.UserCabinet.Models.UserAccount userAccount)
    {
        try
        {
            // Убеждаемся, что Items загружены
            // Если заказ только что создан, Items должны быть в памяти
            // Но если заказ загружен из БД, нужно проверить
            if (order.Items == null || !order.Items.Any())
            {
                _logger.LogWarning("Заказ {OrderId} не содержит Items. Попытка явной загрузки...", order.Id);
                
                // Явная загрузка Items через EF Core
                await _context.Entry(order).Collection(o => o.Items).LoadAsync();
                
                if (order.Items == null || !order.Items.Any())
                {
                    _logger.LogError("Не удалось загрузить Items для заказа {OrderId}", order.Id);
                    return;
                }
            }

            // Получаем контрагента для FimBizContractorId
            var counterparty = await _counterpartyRepository.GetByIdAsync(order.CounterpartyId);
            if (counterparty == null || !counterparty.FimBizContractorId.HasValue)
            {
                _logger.LogWarning("Не удалось отправить заказ {OrderId} в FimBiz: контрагент не имеет FimBizContractorId", order.Id);
                return;
            }

            if (counterparty.FimBizContractorId.Value <= 0)
            {
                _logger.LogError("Не удалось отправить заказ {OrderId}: неверный FimBizContractorId {ContractorId}", 
                    order.Id, counterparty.FimBizContractorId.Value);
                return;
            }

            // Получаем магазин для company_id
            var shop = await _shopRepository.GetByIdAsync(userAccount.ShopId);
            if (shop == null)
            {
                _logger.LogWarning("Не удалось отправить заказ {OrderId} в FimBiz: магазин не найден для пользователя", order.Id);
                return;
            }

            if (shop.FimBizCompanyId <= 0)
            {
                _logger.LogError("Не удалось отправить заказ {OrderId}: неверный FimBizCompanyId {CompanyId}", 
                    order.Id, shop.FimBizCompanyId);
                return;
            }

            // Формируем адрес доставки
            string deliveryAddress = string.Empty;
            if (order.DeliveryAddressId.HasValue)
            {
                var address = await _deliveryAddressRepository.GetByIdAsync(order.DeliveryAddressId.Value);
                if (address != null)
                {
                    var addressParts = new List<string>();
                    if (!string.IsNullOrEmpty(address.Region)) addressParts.Add(address.Region);
                    if (!string.IsNullOrEmpty(address.City)) addressParts.Add(address.City);
                    addressParts.Add(address.Address);
                    if (!string.IsNullOrEmpty(address.Apartment)) addressParts.Add($"кв. {address.Apartment}");
                    if (!string.IsNullOrEmpty(address.PostalCode)) addressParts.Add($"индекс: {address.PostalCode}");
                    deliveryAddress = string.Join(", ", addressParts);
                }
            }

            // Для самовывоза адрес может быть пустым, но лучше указать явно
            if (string.IsNullOrEmpty(deliveryAddress) && order.DeliveryType == Models.DeliveryType.Pickup)
            {
                deliveryAddress = "Самовывоз";
            }

            // Преобразуем DeliveryType из нашей модели в gRPC
            var deliveryType = order.DeliveryType switch
            {
                Models.DeliveryType.Pickup => GrpcDeliveryType.SelfPickup,
                Models.DeliveryType.SellerDelivery => GrpcDeliveryType.CompanyDelivery,
                Models.DeliveryType.Carrier => GrpcDeliveryType.TransportCompany,
                _ => (GrpcDeliveryType)0 // DeliveryTypeUnspecified = 0 (значение по умолчанию в proto3)
            };

            // Проверяем, что есть позиции заказа
            if (!order.Items.Any())
            {
                _logger.LogError("Не удалось отправить заказ {OrderId} в FimBiz: заказ не содержит позиций", order.Id);
                return;
            }

            // Создаем запрос для FimBiz
            var createOrderRequest = new CreateOrderRequest
            {
                CompanyId = shop.FimBizCompanyId,
                ExternalOrderId = order.Id.ToString(),
                ContractorId = counterparty.FimBizContractorId.Value,
                DeliveryAddress = deliveryAddress,
                DeliveryType = deliveryType
            };

            if (shop.FimBizOrganizationId.HasValue && shop.FimBizOrganizationId.Value > 0)
            {
                createOrderRequest.OrganizationId = shop.FimBizOrganizationId.Value;
            }

            // Добавляем позиции заказа
            foreach (var item in order.Items)
            {
                var grpcItem = new GrpcOrderItem
                {
                    Name = item.NomenclatureName,
                    Quantity = item.Quantity,
                    Price = (long)(item.Price * 100), // Цена в копейках
                    IsAvailable = true, // TODO: получить из FimBiz
                    RequiresManufacturing = false // TODO: определить по наличию
                };
                
                // Преобразуем Guid NomenclatureId в int32 для FimBiz
                // Используем первые 4 байта Guid как int32
                // ВАЖНО: Это может давать коллизии, так как Guid - 128 бит, а int32 - 32 бита
                // В идеале нужно хранить маппинг Guid -> int32 в отдельной таблице
                if (item.NomenclatureId != Guid.Empty)
                {
                    var bytes = item.NomenclatureId.ToByteArray();
                    grpcItem.NomenclatureId = BitConverter.ToInt32(bytes, 0);
                    
                    _logger.LogDebug("Отправка позиции заказа: NomenclatureId={NomenclatureId} (из Guid {Guid})", 
                        grpcItem.NomenclatureId, item.NomenclatureId);
                }
                
                createOrderRequest.Items.Add(grpcItem);
            }

            _logger.LogInformation("Отправка заказа {OrderId} в FimBiz. CompanyId: {CompanyId}, ContractorId: {ContractorId}, ItemsCount: {ItemsCount}", 
                order.Id, shop.FimBizCompanyId, counterparty.FimBizContractorId.Value, order.Items.Count);

            // Отправляем в FimBiz
            var response = await _fimBizGrpcClient.CreateOrderAsync(createOrderRequest);

            if (response.Success && response.Order != null)
            {
                // Обновляем заказ с FimBizOrderId
                order.FimBizOrderId = response.Order.OrderId;
                order.OrderNumber = response.Order.OrderNumber;
                order.SyncedWithFimBizAt = DateTime.UtcNow;
                
                // Если вернули трек-номер, сохраняем
                if (!string.IsNullOrEmpty(response.Order.TrackingNumber))
                {
                    order.TrackingNumber = response.Order.TrackingNumber;
                }

                await _orderRepository.UpdateAsync(order);
                
                _logger.LogInformation("Заказ {OrderId} успешно отправлен в FimBiz. FimBizOrderId: {FimBizOrderId}, OrderNumber: {OrderNumber}", 
                    order.Id, order.FimBizOrderId, order.OrderNumber);
            }
            else
            {
                _logger.LogWarning("Не удалось создать заказ {OrderId} в FimBiz: {Message}", 
                    order.Id, response.Message ?? "Неизвестная ошибка");
            }
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при отправке заказа {OrderId} в FimBiz. StatusCode: {StatusCode}, Detail: {Detail}", 
                order.Id, ex.StatusCode, ex.Status.Detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при отправке заказа {OrderId} в FimBiz", order.Id);
        }
    }

    private async Task SendOrderStatusUpdateToFimBizAsync(LocalOrder order, OrderStatus newStatus)
    {
        try
        {
            // Получаем магазин для company_id
            var userAccount = await _userAccountRepository.GetByIdAsync(order.CounterpartyId);
            if (userAccount == null)
            {
                _logger.LogWarning("Не удалось отправить обновление статуса заказа {OrderId}: пользователь не найден", order.Id);
                return;
            }

            var shop = await _shopRepository.GetByIdAsync(userAccount.ShopId);
            if (shop == null || shop.FimBizCompanyId <= 0)
            {
                _logger.LogWarning("Не удалось отправить обновление статуса заказа {OrderId}: магазин не найден или неверный FimBizCompanyId", order.Id);
                return;
            }

            // Преобразуем статус из нашей модели в gRPC
            var grpcStatus = MapToGrpcOrderStatus(newStatus);

            var updateRequest = new UpdateOrderStatusRequest
            {
                ExternalOrderId = order.Id.ToString(),
                CompanyId = shop.FimBizCompanyId,
                NewStatus = grpcStatus
            };

            _logger.LogInformation("Отправка обновления статуса заказа {OrderId} в FimBiz. Новый статус: {Status}", 
                order.Id, newStatus);

            var response = await _fimBizGrpcClient.UpdateOrderStatusAsync(updateRequest);

            if (response.Success)
            {
                _logger.LogInformation("Статус заказа {OrderId} успешно обновлен в FimBiz", order.Id);
            }
            else
            {
                _logger.LogWarning("Не удалось обновить статус заказа {OrderId} в FimBiz: {Message}", 
                    order.Id, response.Message ?? "Неизвестная ошибка");
            }
        }
        catch (Grpc.Core.RpcException ex)
        {
            _logger.LogError(ex, "Ошибка gRPC при обновлении статуса заказа {OrderId} в FimBiz. StatusCode: {StatusCode}, Detail: {Detail}", 
                order.Id, ex.StatusCode, ex.Status.Detail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при обновлении статуса заказа {OrderId} в FimBiz", order.Id);
        }
    }

    private GrpcOrderStatus MapToGrpcOrderStatus(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Processing => GrpcOrderStatus.Processing,
            OrderStatus.AwaitingPayment => GrpcOrderStatus.WaitingForPayment,
            OrderStatus.InvoiceConfirmed => GrpcOrderStatus.BillConfirmed,
            OrderStatus.Manufacturing => GrpcOrderStatus.Manufacturing,
            OrderStatus.Assembling => GrpcOrderStatus.Picking,
            OrderStatus.TransferredToCarrier => GrpcOrderStatus.TransferredToTransport,
            OrderStatus.DeliveringByCarrier => GrpcOrderStatus.DeliveringByTransport,
            OrderStatus.Delivering => GrpcOrderStatus.Delivering,
            OrderStatus.AwaitingPickup => GrpcOrderStatus.AwaitingPickup,
            OrderStatus.Received => GrpcOrderStatus.Completed,
            _ => (GrpcOrderStatus)0 // OrderStatusUnspecified = 0
        };
    }
}
