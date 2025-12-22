using System.Linq;
using System.Text.Json;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Calls;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Infrastructure.Notifications;
using InternetShopService_back.Infrastructure.SignalR;
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
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IServiceProvider _serviceProvider;
    private readonly IShopNotificationService _shopNotificationService;
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
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        IShopNotificationService shopNotificationService)
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
        _serviceProvider = serviceProvider;
        _shopNotificationService = shopNotificationService;
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

        var createdDto = await MapToOrderDtoAsync(order);
        await _shopNotificationService.OrderCreated(order.CounterpartyId, createdDto);

        return createdDto;
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

        var updatedDto = await MapToOrderDtoAsync(order);
        await _shopNotificationService.OrderUpdated(order.CounterpartyId, updatedDto);

        return updatedDto;
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
                .Select(h =>
                {
                    // Логирование для отладки чтения времени из БД
                    _logger.LogDebug(
                        "🔍 [TIME DEBUG] Reading StatusHistory from DB. " +
                        "ChangedAt: {ChangedAt}, " +
                        "DateTime.Kind: {Kind}, " +
                        "OrderId: {OrderId}, Status: {Status}",
                        h.ChangedAt,
                        h.ChangedAt.Kind,
                        order.Id,
                        h.Status);
                    
                    return new OrderStatusHistoryDto
                    {
                        Status = h.Status,
                        StatusName = GetStatusName(h.Status),
                        ChangedAt = h.ChangedAt,
                        Comment = h.Comment
                    };
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

    private static Discount? FindDiscountForItem(int nomenclatureId, List<Discount> discounts)
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

            if (!string.IsNullOrWhiteSpace(order.Carrier))
            {
                createOrderRequest.Carrier = order.Carrier;
            }

            // Передаем delivery_address_id, если адрес был выбран из списка
            if (order.DeliveryAddressId.HasValue)
            {
                createOrderRequest.DeliveryAddressId = order.DeliveryAddressId.Value.ToString();
            }

            if (shop.FimBizOrganizationId.HasValue && shop.FimBizOrganizationId.Value > 0)
            {
                createOrderRequest.OrganizationId = shop.FimBizOrganizationId.Value;
            }

            // НЕ передаем order_number - FimBiz всегда должен генерировать свой номер заказа
            // После получения ответа от FimBiz мы обновим order.OrderNumber значением от FimBiz

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
                
                if (item.NomenclatureId > 0)
                {
                    grpcItem.NomenclatureId = item.NomenclatureId;
                    _logger.LogInformation("Отправка NomenclatureId={NomenclatureId} в FimBiz для позиции {ItemName}", 
                        item.NomenclatureId, item.NomenclatureName);
                }
                else
                {
                    _logger.LogWarning("Позиция заказа имеет некорректный NomenclatureId ({NomenclatureId}). Поле не будет отправлено в FimBiz. OrderId={OrderId}, ItemName={ItemName}", 
                        item.NomenclatureId, order.Id, item.NomenclatureName);
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
                
                // Логируем финальное значение NomenclatureId, которое будет отправлено в FimBiz
                if (grpcItem.HasNomenclatureId && grpcItem.NomenclatureId > 0)
                {
                    _logger.LogInformation("Отправка позиции в FimBiz: NomenclatureId={NomenclatureId}, Name={Name}, Quantity={Quantity}, Price={Price}", 
                        grpcItem.NomenclatureId, grpcItem.Name, grpcItem.Quantity, grpcItem.Price);
                }
                else
                {
                    _logger.LogWarning("Позиция заказа отправляется в FimBiz без NomenclatureId: Name={Name}, Quantity={Quantity}, Price={Price}", 
                        grpcItem.Name, grpcItem.Quantity, grpcItem.Price);
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
                
                // Отправляем неотправленные комментарии в FimBiz
                try
                {
                    var commentService = _serviceProvider.GetRequiredService<IOrderCommentService>();
                    await commentService.SendUnsentCommentsToFimBizAsync(order.Id);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при отправке неотправленных комментариев для заказа {OrderId}", order.Id);
                    // Не прерываем выполнение, заказ уже синхронизирован
                }
                
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
        // Определяем правильный ExternalOrderId в зависимости от того, где был создан заказ
        // Объявляем вне try блока, чтобы использовать в catch
        string externalOrderId;
        
        // Логика определения ExternalOrderId:
        // - Если заказ создан в FimBiz → ExternalOrderId = "FIMBIZ-{FimBizOrderId}"
        // - Если заказ создан у нас → ExternalOrderId = наш Guid (строка)
        //
        // ВАЖНО: Когда заказ создается у нас и отправляется в FimBiz через SendOrderToFimBizAsync,
        // используется ExternalOrderId = order.Id.ToString() (Guid в виде строки).
        // После синхронизации у заказа будет FimBizOrderId, но при обновлении статуса нужно использовать
        // тот же ExternalOrderId, что и при создании (Guid).
        //
        // Проблема: как различить заказ, созданный в FimBiz, от заказа, созданного у нас и синхронизированного?
        // Оба будут иметь FimBizOrderId.
        //
        // ВРЕМЕННОЕ РЕШЕНИЕ: Используем простую логику на основе наличия FimBizOrderId.
        // Если FimBizOrderId есть, считаем заказ созданным в FimBiz.
        // Это может быть неверно для заказов, созданных у нас и синхронизированных.
        // 
        // TODO: Добавить поле IsCreatedInFimBiz в модель Order для точного определения источника заказа.
        
        if (order.FimBizOrderId.HasValue)
        {
            // Заказ создан в FimBiz - используем "FIMBIZ-{FimBizOrderId}"
            externalOrderId = $"FIMBIZ-{order.FimBizOrderId.Value}";
        }
        else
        {
            // Заказ создан у нас - ExternalOrderId это наш Guid (строка)
            externalOrderId = order.Id.ToString();
        }
        
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
            
            // Проверяем формат ExternalOrderId для определения источника заказа
            bool isCreatedInFimBiz = externalOrderId.StartsWith("FIMBIZ-", StringComparison.OrdinalIgnoreCase);
            
            // Если заказ создан у нас и нет FimBizOrderId, значит заказ еще не был отправлен в FimBiz
            // В этом случае не нужно пытаться обновлять статус в FimBiz
            if (!isCreatedInFimBiz && !order.FimBizOrderId.HasValue)
            {
                _logger.LogInformation("=== [STATUS SYNC] Заказ {OrderId} создан у нас, но еще не был отправлен в FimBiz (нет FimBizOrderId). Пропускаем синхронизацию статуса ===", order.Id);
                return false;
            }
            
            if (isCreatedInFimBiz)
            {
                _logger.LogInformation("=== [STATUS SYNC] Заказ синхронизирован с FimBiz, используем ExternalOrderId: {ExternalOrderId} ===", externalOrderId);
            }
            else
            {
                _logger.LogInformation("=== [STATUS SYNC] Заказ создан у нас, используем ExternalOrderId (Guid): {ExternalOrderId} ===", externalOrderId);
            }

            // Специальное логирование для статуса Cancelled
            if (newStatus == OrderStatus.Cancelled)
            {
                _logger.LogInformation("=== [CANCELLED STATUS SYNC] Отправка статуса Cancelled для заказа {OrderId} в FimBiz ===", order.Id);
                _logger.LogInformation("OrderId: {OrderId}, OrderNumber: {OrderNumber}, FimBizOrderId: {FimBizOrderId}, ExternalOrderId: {ExternalOrderId}, IsCreatedInFimBiz: {IsCreatedInFimBiz}, CurrentStatus: {CurrentStatus}, NewStatus: {NewStatus}", 
                    order.Id, order.OrderNumber ?? "не указан", order.FimBizOrderId?.ToString() ?? "не указан", externalOrderId, isCreatedInFimBiz, order.Status, newStatus);
            }

            var updateRequest = new UpdateOrderStatusRequest
            {
                ExternalOrderId = externalOrderId,
                CompanyId = shop.FimBizCompanyId,
                NewStatus = grpcStatus
            };

            _logger.LogInformation("Отправка обновления статуса заказа {OrderId} в FimBiz. Локальный статус: {LocalStatus}, gRPC статус: {GrpcStatus}, ExternalOrderId: {ExternalOrderId}, CompanyId: {CompanyId}, FimBizOrderId: {FimBizOrderId}, IsCreatedInFimBiz: {IsCreatedInFimBiz}", 
                order.Id, newStatus, grpcStatus, updateRequest.ExternalOrderId, updateRequest.CompanyId, order.FimBizOrderId?.ToString() ?? "не указан", isCreatedInFimBiz);

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
            // Если заказ не найден в FimBiz, это нормально для заказов, которые еще не были отправлены
            if (ex.StatusCode == Grpc.Core.StatusCode.NotFound)
            {
                _logger.LogInformation("=== [STATUS SYNC] Заказ {OrderId} не найден в FimBiz. Это нормально, если заказ еще не был отправлен. ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId} ===", 
                    order.Id, externalOrderId, order.FimBizOrderId?.ToString() ?? "отсутствует");
                return false; // Не считаем это ошибкой
            }
            
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

        // Сохраняем изменения через репозиторий с обработкой ошибок конкурентного доступа
        const int maxRetries = 3;
        int retryCount = 0;
        bool updateSuccess = false;
        
        _logger.LogInformation(
            "=== [CANCEL ORDER] Начало отмены заказа {OrderId}. UserId: {UserId}, OldStatus: {OldStatus}, Reason: {Reason}, Попытка: {RetryCount}/{MaxRetries} ===", 
            orderId, userId, oldStatus, reason ?? "не указана", retryCount + 1, maxRetries);
        
        while (retryCount < maxRetries && !updateSuccess)
        {
            try
            {
                _logger.LogDebug(
                    "=== [CANCEL ORDER] Попытка обновления заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). Текущий статус в памяти: {CurrentStatus} ===", 
                    orderId, retryCount + 1, maxRetries, order.Status);
                
                order = await _orderRepository.UpdateAsync(order);
                updateSuccess = true;
                
                _logger.LogInformation(
                    "=== [CANCEL ORDER] Заказ {OrderId} успешно обновлен (попытка {RetryCount}/{MaxRetries}) ===", 
                    orderId, retryCount + 1, maxRetries);
            }
            catch (DbUpdateConcurrencyException ex)
            {
                retryCount++;
                _logger.LogWarning(ex, 
                    "=== [CANCEL ORDER] DbUpdateConcurrencyException при обновлении заказа {OrderId} (попытка {RetryCount}/{MaxRetries}). " +
                    "UserId: {UserId}, Reason: {Reason}. Перезагружаем заказ и повторяем обновление. ===", 
                    orderId, retryCount, maxRetries, userId, reason ?? "не указана");
                
                if (retryCount >= maxRetries)
                {
                    _logger.LogError(ex, 
                        "=== [CANCEL ORDER] Не удалось отменить заказ {OrderId} после {MaxRetries} попыток из-за DbUpdateConcurrencyException. " +
                        "UserId: {UserId}, Reason: {Reason} ===", 
                        orderId, maxRetries, userId, reason ?? "не указана");
                    throw;
                }
                
                // Перезагружаем заказ из БД перед повторной попыткой
                var reloadedOrder = await _orderRepository.GetByIdAsync(orderId);
                if (reloadedOrder == null)
                {
                    _logger.LogError(
                        "=== [CANCEL ORDER] Заказ {OrderId} не найден при перезагрузке после DbUpdateConcurrencyException. " +
                        "UserId: {UserId}, Reason: {Reason}. Возможно, заказ был удалён другим процессом. ===", 
                        orderId, userId, reason ?? "не указана");
                    throw new InvalidOperationException($"Заказ {orderId} не найден в базе данных. Возможно, он был удалён другим процессом.");
                }
                
                // Проверяем, что заказ все еще принадлежит пользователю
                if (reloadedOrder.UserAccountId != userId)
                {
                    _logger.LogWarning(
                        "=== [CANCEL ORDER] Заказ {OrderId} больше не принадлежит пользователю {UserId} после перезагрузки ===", 
                        orderId, userId);
                    throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");
                }
                
                // Проверяем, что заказ еще не отменен другим процессом
                if (reloadedOrder.Status == OrderStatus.Cancelled)
                {
                    _logger.LogInformation(
                        "=== [CANCEL ORDER] Заказ {OrderId} уже отменен другим процессом. Возвращаем текущее состояние. ===", 
                        orderId);
                    order = reloadedOrder;
                    updateSuccess = true;
                    break;
                }
                
                // Проверяем, что статус все еще позволяет отмену
                if (reloadedOrder.Status != OrderStatus.Processing && reloadedOrder.Status != OrderStatus.AwaitingPayment)
                {
                    _logger.LogWarning(
                        "=== [CANCEL ORDER] Статус заказа {OrderId} изменился на {NewStatus} и больше не позволяет отмену. " +
                        "Старый статус: {OldStatus} ===", 
                        orderId, reloadedOrder.Status, oldStatus);
                    throw new InvalidOperationException(
                        $"Статус заказа изменился на '{GetStatusName(reloadedOrder.Status)}'. Отмена заказа возможна только со статусов 'Обрабатывается' или 'Ожидает оплаты'");
                }
                
                // Применяем изменения к перезагруженному заказу
                reloadedOrder.Status = OrderStatus.Cancelled;
                reloadedOrder.UpdatedAt = DateTime.UtcNow;
                
                // Проверяем, нет ли уже записи в истории статусов с таким же статусом и временем
                var existingCancelledHistory = reloadedOrder.StatusHistory?
                    .Where(h => h.Status == OrderStatus.Cancelled)
                    .OrderByDescending(h => h.ChangedAt)
                    .FirstOrDefault();
                
                if (existingCancelledHistory == null)
                {
                    // Добавляем запись в историю статусов только если её еще нет
                    var newStatusHistory = new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = reloadedOrder.Id,
                        Status = OrderStatus.Cancelled,
                        ChangedAt = DateTime.UtcNow,
                        Comment = !string.IsNullOrEmpty(reason) ? $"Отменен пользователем. Причина: {reason}" : "Отменен пользователем"
                    };
                    reloadedOrder.StatusHistory.Add(newStatusHistory);
                    _logger.LogInformation(
                        "Добавлена запись в историю статусов для перезагруженного заказа {OrderId}: {OldStatus} -> Cancelled", 
                        orderId, oldStatus);
                }
                else
                {
                    _logger.LogInformation(
                        "Запись в историю статусов для перезагруженного заказа {OrderId} уже существует, пропускаем добавление", 
                        orderId);
                }
                
                order = reloadedOrder;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("не найден в базе данных"))
            {
                retryCount++;
                _logger.LogError(ex, 
                    "=== [CANCEL ORDER] Заказ {OrderId} не найден в базе данных при попытке обновления (попытка {RetryCount}/{MaxRetries}). " +
                    "UserId: {UserId}, Reason: {Reason}. Возможно, заказ был удалён другим процессом. ===", 
                    orderId, retryCount, maxRetries, userId, reason ?? "не указана");
                
                if (retryCount >= maxRetries)
                {
                    throw;
                }
                
                // Пытаемся найти заказ по ID еще раз
                var retryOrder = await _orderRepository.GetByIdAsync(orderId);
                if (retryOrder == null)
                {
                    throw new InvalidOperationException($"Заказ {orderId} не найден в базе данных. Возможно, он был удалён другим процессом.");
                }
                
                // Проверяем, что заказ принадлежит пользователю
                if (retryOrder.UserAccountId != userId)
                {
                    _logger.LogWarning(
                        "=== [CANCEL ORDER] Заказ {OrderId} больше не принадлежит пользователю {UserId} после перезагрузки ===", 
                        orderId, userId);
                    throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");
                }
                
                // Проверяем, что заказ еще не отменен другим процессом
                if (retryOrder.Status == OrderStatus.Cancelled)
                {
                    _logger.LogInformation(
                        "=== [CANCEL ORDER] Заказ {OrderId} уже отменен другим процессом. Возвращаем текущее состояние. ===", 
                        orderId);
                    order = retryOrder;
                    updateSuccess = true;
                    break;
                }
                
                // Проверяем, что статус все еще позволяет отмену
                if (retryOrder.Status != OrderStatus.Processing && retryOrder.Status != OrderStatus.AwaitingPayment)
                {
                    _logger.LogWarning(
                        "=== [CANCEL ORDER] Статус заказа {OrderId} изменился на {NewStatus} и больше не позволяет отмену. " +
                        "Старый статус: {OldStatus} ===", 
                        orderId, retryOrder.Status, oldStatus);
                    throw new InvalidOperationException(
                        $"Статус заказа изменился на '{GetStatusName(retryOrder.Status)}'. Отмена заказа возможна только со статусов 'Обрабатывается' или 'Ожидает оплаты'");
                }
                
                // Применяем изменения к перезагруженному заказу
                retryOrder.Status = OrderStatus.Cancelled;
                retryOrder.UpdatedAt = DateTime.UtcNow;
                
                // Проверяем, нет ли уже записи в истории статусов
                var existingCancelledHistoryRetry = retryOrder.StatusHistory?
                    .Where(h => h.Status == OrderStatus.Cancelled)
                    .OrderByDescending(h => h.ChangedAt)
                    .FirstOrDefault();
                
                if (existingCancelledHistoryRetry == null)
                {
                    // Добавляем запись в историю статусов только если её еще нет
                    var newStatusHistoryRetry = new OrderStatusHistory
                    {
                        Id = Guid.NewGuid(),
                        OrderId = retryOrder.Id,
                        Status = OrderStatus.Cancelled,
                        ChangedAt = DateTime.UtcNow,
                        Comment = !string.IsNullOrEmpty(reason) ? $"Отменен пользователем. Причина: {reason}" : "Отменен пользователем"
                    };
                    retryOrder.StatusHistory.Add(newStatusHistoryRetry);
                    _logger.LogInformation(
                        "Добавлена запись в историю статусов для перезагруженного заказа {OrderId} (retry): {OldStatus} -> Cancelled", 
                        orderId, oldStatus);
                }
                else
                {
                    _logger.LogInformation(
                        "Запись в историю статусов для перезагруженного заказа {OrderId} (retry) уже существует, пропускаем добавление", 
                        orderId);
                }
                
                order = retryOrder;
            }
        }

        // Перезагружаем заказ из БД с загруженными связанными коллекциями для MapToOrderDtoAsync
        var finalOrder = await _orderRepository.GetByIdAsync(orderId);
        if (finalOrder == null)
        {
            throw new InvalidOperationException($"Заказ {orderId} не найден после обновления");
        }
        order = finalOrder;

        _logger.LogInformation("Заказ {OrderId} отменен пользователем {UserId}. Статус изменен с {OldStatus} на {NewStatus}. Причина: {Reason}", 
            orderId, userId, oldStatus, OrderStatus.Cancelled, reason ?? "не указана");

        // Всегда пытаемся синхронизировать статус с FimBiz
        _logger.LogInformation("=== [CANCEL ORDER] Попытка синхронизации статуса Cancelled для заказа {OrderId} с FimBiz. OrderNumber: {OrderNumber}, FimBizOrderId: {FimBizOrderId}, Причина: {Reason} ===", 
            order.Id, order.OrderNumber ?? "не указан", order.FimBizOrderId?.ToString() ?? "отсутствует", reason ?? "не указана");
        
        bool syncSuccess = false;
        try
        {
            syncSuccess = await SendOrderStatusUpdateToFimBizAsync(order, OrderStatus.Cancelled);
            
            if (syncSuccess)
            {
                _logger.LogInformation("=== [CANCEL ORDER] Статус Cancelled успешно синхронизирован для заказа {OrderId} с FimBiz ===", order.Id);
            }
            else
            {
                // Если не удалось синхронизировать, это может быть нормально для заказов, которые еще не были отправлены в FimBiz
                if (order.FimBizOrderId.HasValue)
                {
                    _logger.LogWarning("=== [CANCEL ORDER] Не удалось синхронизировать статус Cancelled для заказа {OrderId} с FimBiz, но заказ обновлен локально ===", order.Id);
                }
                else
                {
                    _logger.LogInformation("=== [CANCEL ORDER] Не удалось синхронизировать статус для заказа {OrderId} - возможно, заказ еще не был отправлен в FimBiz ===", order.Id);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== [CANCEL ORDER] Ошибка при попытке синхронизации статуса Cancelled для заказа {OrderId} с FimBiz ===", order.Id);
            // Не прерываем выполнение - заказ уже обновлен локально
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

        // Возвращаем DTO (используем перезагруженный заказ с загруженными связанными коллекциями)
        return await MapToOrderDtoAsync(order);
    }
}
