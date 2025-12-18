using System.Text.Json;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Calls;
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
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using OrderStatus = InternetShopService_back.Modules.OrderManagement.Models.OrderStatus;
using GrpcOrder = InternetShopService_back.Infrastructure.Grpc.Orders.Order;
using GrpcOrderStatus = InternetShopService_back.Infrastructure.Grpc.Orders.OrderStatus;
using GrpcDeliveryType = InternetShopService_back.Infrastructure.Grpc.Orders.DeliveryType;
using GrpcOrderItem = InternetShopService_back.Infrastructure.Grpc.Orders.OrderItem;
using GrpcBillInfo = InternetShopService_back.Infrastructure.Grpc.Orders.BillInfo;
using GrpcBillStatus = InternetShopService_back.Infrastructure.Grpc.Orders.BillStatus;
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
    private readonly ICallService _callService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderService> _logger;
    private readonly IConfiguration _configuration;
    private const int _codeExpirationMinutes = 30; // Время действия кода подтверждения

    public OrderService(
        IOrderRepository orderRepository,
        IUserAccountRepository userAccountRepository,
        ICounterpartyRepository counterpartyRepository,
        IDeliveryAddressRepository deliveryAddressRepository,
        ICargoReceiverRepository cargoReceiverRepository,
        IShopRepository shopRepository,
        IFimBizGrpcClient fimBizGrpcClient,
        IEmailService emailService,
        ICallService callService,
        ApplicationDbContext context,
        ILogger<OrderService> logger,
        IConfiguration configuration)
    {
        _orderRepository = orderRepository;
        _userAccountRepository = userAccountRepository;
        _counterpartyRepository = counterpartyRepository;
        _deliveryAddressRepository = deliveryAddressRepository;
        _cargoReceiverRepository = cargoReceiverRepository;
        _shopRepository = shopRepository;
        _fimBizGrpcClient = fimBizGrpcClient;
        _emailService = emailService;
        _callService = callService;
        _context = context;
        _logger = logger;
        _configuration = configuration;
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
                UrlPhotosJson = SerializeUrlPhotos(itemDto.UrlPhotos),
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

        // Сохраняем последний использованный способ доставки
        userAccount.LastDeliveryType = dto.DeliveryType;
        await _userAccountRepository.UpdateAsync(userAccount);

        _logger.LogInformation("Создан заказ {OrderId} для пользователя {UserId}. Сохранен способ доставки: {DeliveryType}", 
            order.Id, userId, dto.DeliveryType);

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

    public async Task<PagedResult<OrderDto>> GetOrdersByUserPagedAsync(Guid userId, int page, int pageSize)
    {
        // Валидация параметров
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 10;
        if (pageSize > 100) pageSize = 100; // Максимальный размер страницы

        var (orders, totalCount) = await _orderRepository.GetByUserIdPagedAsync(userId, page, pageSize);
        var orderDtos = new List<OrderDto>();

        foreach (var order in orders)
        {
            orderDtos.Add(await MapToOrderDtoAsync(order));
        }

        return new PagedResult<OrderDto>
        {
            Items = orderDtos,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
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

        // Специальное логирование для статуса Cancelled
        if (status == OrderStatus.Cancelled)
        {
            _logger.LogInformation("=== [CANCELLED STATUS UPDATE] Заказ {OrderId} переведен в статус Cancelled. Старый статус: {OldStatus} ===", 
                orderId, oldStatus);
        }

        // Если заказ синхронизирован с FimBiz, отправляем обновление статуса
        if (order.FimBizOrderId.HasValue)
        {
            if (status == OrderStatus.Cancelled)
            {
                _logger.LogInformation("=== [CANCELLED STATUS SYNC] Попытка синхронизации статуса Cancelled для заказа {OrderId} с FimBiz. FimBizOrderId: {FimBizOrderId} ===", 
                    order.Id, order.FimBizOrderId.Value);
            }
            var syncSuccess = await SendOrderStatusUpdateToFimBizAsync(order, status);
            if (!syncSuccess)
            {
                if (status == OrderStatus.Cancelled)
                {
                    _logger.LogWarning("=== [CANCELLED STATUS SYNC] Не удалось синхронизировать статус Cancelled для заказа {OrderId} с FimBiz, но заказ обновлен локально ===", order.Id);
                }
                _logger.LogWarning("Не удалось отправить обновление статуса заказа {OrderId} в FimBiz, но заказ обновлен локально", order.Id);
            }
            else if (status == OrderStatus.Cancelled)
            {
                _logger.LogInformation("=== [CANCELLED STATUS SYNC] Статус Cancelled успешно синхронизирован для заказа {OrderId} с FimBiz ===", order.Id);
            }
        }
        else
        {
            if (status == OrderStatus.Cancelled)
            {
                _logger.LogWarning("=== [CANCELLED STATUS SYNC] Невозможно синхронизировать статус Cancelled для заказа {OrderId} с FimBiz: FimBizOrderId отсутствует ===", order.Id);
            }
            _logger.LogDebug("Заказ {OrderId} не синхронизирован с FimBiz (FimBizOrderId отсутствует), синхронизация статуса пропущена", order.Id);
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
        // Уведомляем только в ключевых статусах согласно ТЗ:
        // - когда заказ перешел на ожидание оплаты
        // - когда заказ перешел на ожидание получения
        return status == OrderStatus.AwaitingPayment ||
               status == OrderStatus.AwaitingPickup;
    }

    private async Task<OrderDto> MapToOrderDtoAsync(LocalOrder order)
    {
        // Защита от null коллекций
        var items = order.Items ?? new List<LocalOrderItem>();
        var attachments = order.Attachments ?? new List<OrderAttachment>();
        var statusHistory = order.StatusHistory ?? new List<OrderStatusHistory>();

        var dto = new OrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber ?? string.Empty,
            Status = order.Status,
            StatusName = GetStatusName(order.Status),
            DeliveryType = order.DeliveryType,
            TrackingNumber = order.TrackingNumber,
            Carrier = order.Carrier,
            TotalAmount = order.TotalAmount,
            CreatedAt = order.CreatedAt,
            Items = items.Select(i => new OrderItemDto
            {
                Id = i.Id,
                NomenclatureId = i.NomenclatureId,
                NomenclatureName = i.NomenclatureName ?? string.Empty,
                Quantity = i.Quantity,
                Price = i.Price,
                DiscountPercent = i.DiscountPercent,
                TotalAmount = i.TotalAmount,
                UrlPhotos = DeserializeUrlPhotos(i.UrlPhotosJson)
            }).ToList(),
            Attachments = attachments.Select(a => new OrderAttachmentDto
            {
                Id = a.Id,
                FileName = a.FileName ?? string.Empty,
                ContentType = a.ContentType,
                IsVisibleToCustomer = a.IsVisibleToCustomer,
                CreatedAt = a.CreatedAt
            }).ToList(),
            StatusHistory = statusHistory
                .OrderBy(h => h.ChangedAt)
                .Select(h => new OrderStatusHistoryDto
                {
                    Status = h.Status,
                    StatusName = GetStatusName(h.Status),
                    ChangedAt = h.ChangedAt,
                    Comment = h.Comment
                }).ToList()
        };

        // Загружаем адрес доставки, если есть
        if (order.DeliveryAddressId.HasValue)
        {
            try
            {
                var address = await _deliveryAddressRepository.GetByIdAsync(order.DeliveryAddressId.Value);
                if (address != null)
                {
                    dto.DeliveryAddress = new OrderManagement.DTOs.DeliveryAddressDto
                    {
                        Id = address.Id,
                        Address = address.Address ?? string.Empty,
                        City = address.City ?? string.Empty,
                        Region = address.Region,
                        PostalCode = address.PostalCode
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при загрузке адреса доставки {AddressId} для заказа {OrderId}", 
                    order.DeliveryAddressId.Value, order.Id);
                // Продолжаем выполнение без адреса
            }
        }

        // Загружаем грузополучателя, если есть
        if (order.CargoReceiverId.HasValue)
        {
            try
            {
                var receiver = await _cargoReceiverRepository.GetByIdAsync(order.CargoReceiverId.Value);
                if (receiver != null)
                {
                    dto.CargoReceiver = new OrderManagement.DTOs.CargoReceiverDto
                    {
                        Id = receiver.Id,
                        FullName = receiver.FullName ?? string.Empty,
                        PassportSeries = receiver.PassportSeries,
                        PassportNumber = receiver.PassportNumber
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Ошибка при загрузке грузополучателя {ReceiverId} для заказа {OrderId}", 
                    order.CargoReceiverId.Value, order.Id);
                // Продолжаем выполнение без грузополучателя
            }
        }

        // Загружаем информацию о счете, если есть - только относительный URL
        if (order.InvoiceId.HasValue)
        {
            var invoice = await _context.Invoices
                .FirstOrDefaultAsync(i => i.Id == order.InvoiceId.Value);
            
            if (invoice != null && !string.IsNullOrEmpty(invoice.PdfUrl))
            {
                dto.Invoice = new InvoiceInfoDto
                {
                    PdfUrl = invoice.PdfUrl // Относительный URL передаем как есть
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
            OrderStatus.Cancelled => "Отменен",
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
                
                // Добавляем UrlPhotos товара напрямую в OrderItem
                if (!string.IsNullOrWhiteSpace(item.UrlPhotosJson))
                {
                    var urlPhotos = DeserializeUrlPhotos(item.UrlPhotosJson);
                    if (urlPhotos != null && urlPhotos.Any())
                    {
                        grpcItem.PhotoUrls.AddRange(urlPhotos);
                    }
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

                // Обрабатываем bill_info, если счет был создан автоматически
                if (response.BillInfo != null)
                {
                    await ProcessBillInfoFromCreateOrderAsync(order, response.BillInfo);
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

    private async Task<bool> SendOrderStatusUpdateToFimBizAsync(LocalOrder order, OrderStatus newStatus)
    {
        try
        {
            // Получаем магазин для company_id
            var userAccount = await _userAccountRepository.GetByIdAsync(order.UserAccountId);
            if (userAccount == null)
            {
                _logger.LogWarning("Не удалось отправить обновление статуса заказа {OrderId}: пользователь не найден", order.Id);
                return false;
            }

            var shop = await _shopRepository.GetByIdAsync(userAccount.ShopId);
            if (shop == null || shop.FimBizCompanyId <= 0)
            {
                _logger.LogWarning("Не удалось отправить обновление статуса заказа {OrderId}: магазин не найден или неверный FimBizCompanyId", order.Id);
                return false;
            }

            // Преобразуем статус из нашей модели в gRPC
            var grpcStatus = MapToGrpcOrderStatus(newStatus);

            // Специальное логирование для статуса Cancelled
            if (newStatus == OrderStatus.Cancelled)
            {
                _logger.LogInformation("=== [CANCELLED STATUS SYNC] Отправка статуса Cancelled для заказа {OrderId} в FimBiz ===", order.Id);
                _logger.LogInformation("OrderId: {OrderId}, OrderNumber: {OrderNumber}, FimBizOrderId: {FimBizOrderId}, CurrentStatus: {CurrentStatus}, NewStatus: {NewStatus}", 
                    order.Id, order.OrderNumber ?? "не указан", order.FimBizOrderId?.ToString() ?? "не указан", order.Status, newStatus);
            }

            var updateRequest = new UpdateOrderStatusRequest
            {
                ExternalOrderId = order.Id.ToString(),
                CompanyId = shop.FimBizCompanyId,
                NewStatus = grpcStatus
            };

            _logger.LogInformation("Отправка обновления статуса заказа {OrderId} в FimBiz. Локальный статус: {LocalStatus}, gRPC статус: {GrpcStatus}, ExternalOrderId: {ExternalOrderId}, CompanyId: {CompanyId}, FimBizOrderId: {FimBizOrderId}", 
                order.Id, newStatus, grpcStatus, updateRequest.ExternalOrderId, updateRequest.CompanyId, order.FimBizOrderId?.ToString() ?? "не указан");

            var response = await _fimBizGrpcClient.UpdateOrderStatusAsync(updateRequest);

            if (response.Success)
            {
                if (newStatus == OrderStatus.Cancelled)
                {
                    _logger.LogInformation("=== [CANCELLED STATUS SYNC] Статус Cancelled успешно отправлен в FimBiz для заказа {OrderId} ===", order.Id);
                }
                _logger.LogInformation("Статус заказа {OrderId} успешно обновлен в FimBiz. Response.Success: {Success}, Response.Message: {Message}", 
                    order.Id, response.Success, response.Message ?? "нет сообщения");
                return true;
            }
            else
            {
                if (newStatus == OrderStatus.Cancelled)
                {
                    _logger.LogWarning("=== [CANCELLED STATUS SYNC] Не удалось отправить статус Cancelled в FimBiz для заказа {OrderId} ===", order.Id);
                    _logger.LogWarning("Response.Success: {Success}, Response.Message: {Message}", response.Success, response.Message ?? "Неизвестная ошибка");
                }
                _logger.LogWarning("Не удалось обновить статус заказа {OrderId} в FimBiz. Response.Success: {Success}, Response.Message: {Message}", 
                    order.Id, response.Success, response.Message ?? "Неизвестная ошибка");
                return false;
            }
        }
        catch (Grpc.Core.RpcException ex)
        {
            if (newStatus == OrderStatus.Cancelled)
            {
                _logger.LogError(ex, "=== [CANCELLED STATUS SYNC] Ошибка gRPC при отправке статуса Cancelled для заказа {OrderId} в FimBiz ===", order.Id);
                _logger.LogError("StatusCode: {StatusCode}, Detail: {Detail}, Message: {Message}", ex.StatusCode, ex.Status.Detail, ex.Message);
            }
            _logger.LogError(ex, "Ошибка gRPC при обновлении статуса заказа {OrderId} в FimBiz. StatusCode: {StatusCode}, Detail: {Detail}", 
                order.Id, ex.StatusCode, ex.Status.Detail);
            return false;
        }
        catch (Exception ex)
        {
            if (newStatus == OrderStatus.Cancelled)
            {
                _logger.LogError(ex, "=== [CANCELLED STATUS SYNC] Неожиданная ошибка при отправке статуса Cancelled для заказа {OrderId} в FimBiz ===", order.Id);
            }
            _logger.LogError(ex, "Неожиданная ошибка при обновлении статуса заказа {OrderId} в FimBiz", order.Id);
            return false;
        }
    }

    private GrpcOrderStatus MapToGrpcOrderStatus(OrderStatus status)
    {
        return status switch
        {
            OrderStatus.Processing => GrpcOrderStatus.Processing,
            OrderStatus.AwaitingPayment => GrpcOrderStatus.WaitingForPayment,
            OrderStatus.InvoiceConfirmed => GrpcOrderStatus.PaymentConfirmed,
            OrderStatus.Manufacturing => GrpcOrderStatus.Manufacturing,
            OrderStatus.Assembling => GrpcOrderStatus.Picking,
            OrderStatus.TransferredToCarrier => GrpcOrderStatus.TransferredToTransport,
            OrderStatus.DeliveringByCarrier => GrpcOrderStatus.DeliveringByTransport,
            OrderStatus.Delivering => GrpcOrderStatus.Delivering,
            OrderStatus.AwaitingPickup => GrpcOrderStatus.AwaitingPickup,
            OrderStatus.Received => GrpcOrderStatus.Completed,
            OrderStatus.Cancelled => GrpcOrderStatus.Cancelled,
            _ => (GrpcOrderStatus)0 // OrderStatusUnspecified = 0
        };
    }

    /// <summary>
    /// Обработка информации о счете (bill_info) при создании заказа - сохраняем только относительный URL PDF
    /// </summary>
    private async Task ProcessBillInfoFromCreateOrderAsync(LocalOrder order, GrpcBillInfo billInfo)
    {
        try
        {
            // Сохраняем только относительный URL - фронт сам обработает
            string? pdfUrl = billInfo.PdfUrl;

            // Проверяем, существует ли уже счет для этого заказа
            var existingInvoice = await _context.Invoices
                .FirstOrDefaultAsync(i => i.OrderId == order.Id);

            bool isNewInvoice = existingInvoice == null;

            if (existingInvoice != null)
            {
                // Обновляем существующий счет - только URL
                existingInvoice.PdfUrl = pdfUrl;
                existingInvoice.UpdatedAt = DateTime.UtcNow;

                _logger.LogInformation("Обновлен счет для заказа {OrderId}. InvoiceId: {InvoiceId}, PdfUrl: {PdfUrl}", 
                    order.Id, existingInvoice.Id, pdfUrl ?? "не указан");
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

                await _context.Invoices.AddAsync(invoice);
                order.InvoiceId = invoice.Id;

                _logger.LogInformation("Создан новый счет для заказа {OrderId}. InvoiceId: {InvoiceId}, PdfUrl: {PdfUrl}", 
                    order.Id, invoice.Id, pdfUrl ?? "не указан");
            }

            await _context.SaveChangesAsync();

            // Отправляем уведомление контрагенту о создании/обновлении счета
            // Для email формируем полный URL, если он относительный
            if (isNewInvoice || !string.IsNullOrEmpty(pdfUrl))
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
                orderNumber,
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
    /// Десериализация JSON строки в список URL фотографий
    /// </summary>
    private List<string> DeserializeUrlPhotos(string? urlPhotosJson)
    {
        if (string.IsNullOrWhiteSpace(urlPhotosJson))
        {
            return new List<string>();
        }

        try
        {
            var result = JsonSerializer.Deserialize<List<string>>(urlPhotosJson);
            return result ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task RequestInvoiceConfirmationCodeAsync(Guid orderId, Guid userId)
    {
        // Получаем заказ
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            throw new InvalidOperationException("Заказ не найден");

        // Проверяем, что заказ принадлежит пользователю
        if (order.UserAccountId != userId)
            throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");

        // Проверяем статус заказа - должен быть AwaitingPayment
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("Подтверждение счета возможно только для заказов со статусом 'Ожидает оплаты/Подтверждения счета'");

        // Получаем контрагента
        var counterparty = await _counterpartyRepository.GetByIdAsync(order.CounterpartyId);
        if (counterparty == null)
            throw new InvalidOperationException("Контрагент не найден");

        // Проверяем, что у контрагента есть постоплата
        if (!counterparty.HasPostPayment)
            throw new InvalidOperationException("Подтверждение счета через звонок доступно только для контрагентов с постоплатой");

        // Получаем пользователя для получения номера телефона
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
            throw new InvalidOperationException("Пользователь не найден");

        if (string.IsNullOrEmpty(userAccount.PhoneNumber))
            throw new InvalidOperationException("Номер телефона пользователя не указан");

        // Отправляем звонок
        var callRequest = new CallRequestDto { PhoneNumber = userAccount.PhoneNumber };
        var callResult = await _callService.SendCallAndUpdateUserAsync(callRequest, userAccount);

        if (!callResult.Success)
        {
            if (callResult.IsCallLimitExceeded)
            {
                throw new InvalidOperationException(
                    $"Заявки на звонок были исчерпаны. Попробуйте ещё раз через {callResult.RemainingWaitTimeMinutes} минут");
            }
            throw new InvalidOperationException(callResult.Message ?? "Не удалось отправить звонок");
        }

        if (string.IsNullOrEmpty(callResult.LastFourDigits))
        {
            throw new InvalidOperationException("Не удалось получить код подтверждения");
        }

        // Сохраняем обновленного пользователя
        await _userAccountRepository.UpdateAsync(userAccount);

        _logger.LogInformation("Код подтверждения счета отправлен на номер {PhoneNumber} для заказа {OrderId}", 
            userAccount.PhoneNumber, orderId);
    }

    public async Task<OrderDto> ConfirmInvoiceByPhoneAsync(Guid orderId, Guid userId, string code)
    {
        // Получаем заказ
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            throw new InvalidOperationException("Заказ не найден");

        // Проверяем, что заказ принадлежит пользователю
        if (order.UserAccountId != userId)
            throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");

        // Проверяем статус заказа - должен быть AwaitingPayment
        if (order.Status != OrderStatus.AwaitingPayment)
            throw new InvalidOperationException("Подтверждение счета возможно только для заказов со статусом 'Ожидает оплаты/Подтверждения счета'");

        // Получаем контрагента
        var counterparty = await _counterpartyRepository.GetByIdAsync(order.CounterpartyId);
        if (counterparty == null)
            throw new InvalidOperationException("Контрагент не найден");

        // Проверяем, что у контрагента есть постоплата
        if (!counterparty.HasPostPayment)
            throw new InvalidOperationException("Подтверждение счета через звонок доступно только для контрагентов с постоплатой");

        // Получаем пользователя для проверки кода
        var userAccount = await _userAccountRepository.GetByIdAsync(userId);
        if (userAccount == null)
            throw new InvalidOperationException("Пользователь не найден");

        // Проверка кода
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(userAccount.PhoneCallDigits))
        {
            throw new UnauthorizedAccessException("Неверный код подтверждения");
        }

        // Проверка времени действия кода (30 минут)
        if (!userAccount.PhoneCallDateTime.HasValue ||
            (DateTime.UtcNow - userAccount.PhoneCallDateTime.Value).TotalMinutes > _codeExpirationMinutes)
        {
            throw new UnauthorizedAccessException("Истекло время действия кода. Запросите новый звонок");
        }

        // Сравнение кода
        if (userAccount.PhoneCallDigits != code)
        {
            throw new UnauthorizedAccessException("Неверный код подтверждения");
        }

        // Очистка кода после успешной проверки
        userAccount.PhoneCallDigits = null;
        userAccount.PhoneCallDateTime = null;
        await _userAccountRepository.UpdateAsync(userAccount);

        // Обновляем статус заказа на InvoiceConfirmed
        var oldStatus = order.Status;
        order.Status = OrderStatus.InvoiceConfirmed;
        order.UpdatedAt = DateTime.UtcNow;

        // Добавляем запись в историю статусов
        var statusHistory = new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = OrderStatus.InvoiceConfirmed,
            ChangedAt = DateTime.UtcNow
        };
        order.StatusHistory.Add(statusHistory);

        order = await _orderRepository.UpdateAsync(order);

        _logger.LogInformation("Счет для заказа {OrderId} подтвержден пользователем {UserId}. Статус изменен с {OldStatus} на {NewStatus}", 
            orderId, userId, oldStatus, order.Status);

        // Если заказ синхронизирован с FimBiz, отправляем обновление статуса
        if (order.FimBizOrderId.HasValue)
        {
            var syncSuccess = await SendOrderStatusUpdateToFimBizAsync(order, OrderStatus.InvoiceConfirmed);
            if (!syncSuccess)
            {
                _logger.LogWarning("Не удалось отправить обновление статуса подтверждения счета заказа {OrderId} в FimBiz, но заказ обновлен локально", order.Id);
            }
        }

        // Отправляем уведомление на email контрагента
        await SendOrderStatusNotificationAsync(order);

        return await MapToOrderDtoAsync(order);
    }

    /// <summary>
    /// Загружает файл к заказу
    /// </summary>
    public async Task<OrderAttachmentDto> UploadAttachmentAsync(Guid orderId, Guid userId, IFormFile file)
    {
        // Проверяем, что файл передан
        if (file == null || file.Length == 0)
        {
            throw new InvalidOperationException("Файл не указан или пуст");
        }

        // Проверяем размер файла (максимум 50 МБ)
        const long maxFileSize = 50 * 1024 * 1024; // 50 МБ
        if (file.Length > maxFileSize)
        {
            throw new InvalidOperationException($"Размер файла превышает максимально допустимый ({maxFileSize / 1024 / 1024} МБ)");
        }

        // Получаем заказ
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
        {
            throw new InvalidOperationException("Заказ не найден");
        }

        // Проверяем, что заказ принадлежит пользователю
        if (order.UserAccountId != userId)
        {
            throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");
        }

        // Сохраняем файл локально
        var localPath = await SaveFileLocallyAsync(orderId, file.FileName, file);
        if (string.IsNullOrEmpty(localPath))
        {
            throw new InvalidOperationException("Не удалось сохранить файл");
        }

        // Создаем запись в БД
        var attachment = new OrderAttachment
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            FileName = file.FileName,
            FilePath = localPath,
            ContentType = file.ContentType,
            FileSize = file.Length,
            IsVisibleToCustomer = true, // По умолчанию файлы, загруженные пользователем, видимы ему
            CreatedAt = DateTime.UtcNow
        };

        await _context.OrderAttachments.AddAsync(attachment);
        await _context.SaveChangesAsync();

        _logger.LogInformation("Файл {FileName} успешно загружен для заказа {OrderId} пользователем {UserId}", 
            file.FileName, orderId, userId);

        return new OrderAttachmentDto
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            IsVisibleToCustomer = attachment.IsVisibleToCustomer,
            CreatedAt = attachment.CreatedAt
        };
    }

    /// <summary>
    /// Сохранение файла локально (из IFormFile)
    /// </summary>
    private async Task<string?> SaveFileLocallyAsync(Guid orderId, string fileName, IFormFile file)
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
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

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
    /// Отменяет заказ (разрешено только со статусов Processing и AwaitingPayment)
    /// </summary>
    public async Task<OrderDto> CancelOrderAsync(Guid orderId, Guid userId, string? reason)
    {
        // Получаем заказ
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
            throw new InvalidOperationException("Заказ не найден");

        // Проверяем, что заказ принадлежит пользователю
        if (order.UserAccountId != userId)
            throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");

        // Валидация: отмена разрешена только со статусов Processing и AwaitingPayment
        if (order.Status != OrderStatus.Processing && order.Status != OrderStatus.AwaitingPayment)
        {
            throw new InvalidOperationException(
                "Отмена заказа возможна только со статусов 'Обрабатывается' или 'Ожидает оплаты'");
        }

        // Проверяем, что заказ уже не отменен
        if (order.Status == OrderStatus.Cancelled)
        {
            throw new InvalidOperationException("Заказ уже отменен");
        }

        var oldStatus = order.Status;
        
        // Обновляем статус заказа
        order.Status = OrderStatus.Cancelled;
        order.UpdatedAt = DateTime.UtcNow;

        // Добавляем запись в историю статусов
        var statusHistory = new OrderStatusHistory
        {
            Id = Guid.NewGuid(),
            OrderId = order.Id,
            Status = OrderStatus.Cancelled,
            ChangedAt = DateTime.UtcNow,
            Comment = !string.IsNullOrEmpty(reason) ? $"Отменен пользователем. Причина: {reason}" : "Отменен пользователем"
        };
        order.StatusHistory.Add(statusHistory);

        // Сохраняем изменения через репозиторий (как в других методах)
        order = await _orderRepository.UpdateAsync(order);

        _logger.LogInformation("Заказ {OrderId} отменен пользователем {UserId}. Статус изменен с {OldStatus} на {NewStatus}. Причина: {Reason}", 
            orderId, userId, oldStatus, OrderStatus.Cancelled, reason ?? "не указана");

        // Если заказ синхронизирован с FimBiz, отправляем обновление статуса (без перезагрузки)
        if (order.FimBizOrderId.HasValue)
        {
            _logger.LogInformation("=== [CANCELLED STATUS SYNC] Попытка синхронизации статуса Cancelled для заказа {OrderId} (отменен пользователем) с FimBiz. FimBizOrderId: {FimBizOrderId}, Причина: {Reason} ===", 
                order.Id, order.FimBizOrderId.Value, reason ?? "не указана");
            var syncSuccess = await SendOrderStatusUpdateToFimBizAsync(order, OrderStatus.Cancelled);
            if (!syncSuccess)
            {
                _logger.LogWarning("=== [CANCELLED STATUS SYNC] Не удалось синхронизировать статус Cancelled для заказа {OrderId} (отменен пользователем) с FimBiz, но заказ обновлен локально ===", order.Id);
                _logger.LogWarning("Не удалось отправить обновление статуса заказа {OrderId} в FimBiz, но заказ обновлен локально", order.Id);
            }
            else
            {
                _logger.LogInformation("=== [CANCELLED STATUS SYNC] Статус Cancelled успешно синхронизирован для заказа {OrderId} (отменен пользователем) с FimBiz ===", order.Id);
            }
        }
        else
        {
            _logger.LogWarning("=== [CANCELLED STATUS SYNC] Невозможно синхронизировать статус Cancelled для заказа {OrderId} (отменен пользователем) с FimBiz: FimBizOrderId отсутствует ===", order.Id);
            _logger.LogDebug("Заказ {OrderId} не синхронизирован с FimBiz (FimBizOrderId отсутствует), синхронизация статуса пропущена", order.Id);
        }

        // Отправляем уведомление на email контрагента об отмене заказа
        try
        {
            var counterparty = await _counterpartyRepository.GetByIdAsync(order.CounterpartyId);
            if (counterparty != null && !string.IsNullOrEmpty(counterparty.Email))
            {
                // Используем OrderNumber или fallback на ID, если OrderNumber пустой
                var orderNumber = !string.IsNullOrEmpty(order.OrderNumber) 
                    ? order.OrderNumber 
                    : order.Id.ToString();
                
                await _emailService.SendOrderCancellationNotificationAsync(
                    counterparty.Email,
                    order.Id,
                    orderNumber,
                    reason);
                
                _logger.LogInformation("Отправлено уведомление об отмене заказа {OrderId} на email {Email}", 
                    order.Id, counterparty.Email);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке уведомления об отмене заказа {OrderId}", orderId);
            // Не прерываем выполнение при ошибке отправки уведомления
        }

        // Возвращаем DTO (используем объект order после UpdateAsync, как в других методах)
        return await MapToOrderDtoAsync(order);
    }
}
