using InternetShopService_back.Data;
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

namespace InternetShopService_back.Modules.OrderManagement.Services;

public class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IUserAccountRepository _userAccountRepository;
    private readonly ICounterpartyRepository _counterpartyRepository;
    private readonly IDeliveryAddressRepository _deliveryAddressRepository;
    private readonly ICargoReceiverRepository _cargoReceiverRepository;
    private readonly IEmailService _emailService;
    private readonly ApplicationDbContext _context;
    private readonly ILogger<OrderService> _logger;

    public OrderService(
        IOrderRepository orderRepository,
        IUserAccountRepository userAccountRepository,
        ICounterpartyRepository counterpartyRepository,
        IDeliveryAddressRepository deliveryAddressRepository,
        ICargoReceiverRepository cargoReceiverRepository,
        IEmailService emailService,
        ApplicationDbContext context,
        ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _userAccountRepository = userAccountRepository;
        _counterpartyRepository = counterpartyRepository;
        _deliveryAddressRepository = deliveryAddressRepository;
        _cargoReceiverRepository = cargoReceiverRepository;
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
        var order = new Order
        {
            Id = Guid.NewGuid(),
            UserAccountId = userId,
            CounterpartyId = userAccount.CounterpartyId,
            Status = OrderStatus.Processing,
            DeliveryType = dto.DeliveryType,
            DeliveryAddressId = dto.DeliveryAddressId,
            CargoReceiverId = dto.CargoReceiverId,
            CarrierId = dto.CarrierId,
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

            var orderItem = new OrderItem
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

        // Отправляем уведомление на email контрагента при изменении статуса
        await SendOrderStatusNotificationAsync(order);

        return await MapToOrderDtoAsync(order);
    }

    private async Task SendOrderStatusNotificationAsync(Order order)
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

    private async Task<OrderDto> MapToOrderDtoAsync(Order order)
    {
        var dto = new OrderDto
        {
            Id = order.Id,
            OrderNumber = order.OrderNumber,
            Status = order.Status,
            StatusName = GetStatusName(order.Status),
            DeliveryType = order.DeliveryType,
            TrackingNumber = order.TrackingNumber,
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
}
