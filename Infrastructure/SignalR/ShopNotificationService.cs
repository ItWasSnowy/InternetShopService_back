using System;
using System.Threading;
using System.Threading.Tasks;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Events;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.Notifications.DTOs;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using UserCabinetDeliveryAddressDto = InternetShopService_back.Modules.UserCabinet.DTOs.DeliveryAddressDto;
using UserCabinetCargoReceiverDto = InternetShopService_back.Modules.UserCabinet.DTOs.CargoReceiverDto;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.SignalR;

public sealed class ShopNotificationService : IShopNotificationService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly EventNotificationService _eventNotificationService;
    private readonly ILogger<ShopNotificationService> _logger;

    public ShopNotificationService(
        ApplicationDbContext dbContext,
        EventNotificationService eventNotificationService,
        ILogger<ShopNotificationService> logger)
    {
        _dbContext = dbContext;
        _eventNotificationService = eventNotificationService;
        _logger = logger;
    }

    private async Task EmitAsync(Guid counterpartyId, EventType eventType, Guid entityId, object data)
    {
        // В текущей схеме SignalR UserId == Guid userId из ClaimTypes.NameIdentifier.
        // Сервисы вызывают события в разрезе CounterpartyId, поэтому здесь маппим counterparty -> userId.
        // В проекте сейчас 1 кабинет на контрагента (one-to-one Counterparty <-> UserAccount).
        var userId = await _dbContext.UserAccounts
            .AsNoTracking()
            .Where(u => u.CounterpartyId == counterpartyId)
            .Select(u => (Guid?)u.Id)
            .FirstOrDefaultAsync();

        if (!userId.HasValue)
        {
            _logger.LogWarning(
                "Cannot emit event because no UserAccount found for CounterpartyId={CounterpartyId}. EventType={EventType}, EntityId={EntityId}",
                counterpartyId,
                eventType,
                entityId);
            return;
        }

        await _eventNotificationService.NotifyEventAsync(userId.Value.ToString(), eventType, entityId, data);
    }

    public Task OrderCreated(Guid counterpartyId, OrderDto order)
        => EmitAsync(counterpartyId, EventType.OrderCreated, order.Id, order);

    public Task OrderUpdated(Guid counterpartyId, OrderDto order)
        => EmitAsync(counterpartyId, EventType.OrderUpdated, order.Id, order);

    public Task OrderDeleted(Guid counterpartyId, Guid orderId)
        => EmitAsync(counterpartyId, EventType.OrderUpdated, orderId, new { OrderId = orderId });

    public Task OrderCommentAdded(Guid counterpartyId, OrderCommentDto comment)
        => EmitAsync(counterpartyId, EventType.OrderCommentAdded, comment.OrderId, comment);

    public Task CounterpartyUpdated(Guid counterpartyId, CounterpartyDto counterparty)
        => EmitAsync(counterpartyId, EventType.CounterpartyUpdated, counterparty.Id, counterparty);

    public Task DeliveryAddressCreated(Guid counterpartyId, UserCabinetDeliveryAddressDto address)
        => EmitAsync(counterpartyId, EventType.DeliveryAddressCreated, address.Id, address);

    public Task DeliveryAddressUpdated(Guid counterpartyId, UserCabinetDeliveryAddressDto address)
        => EmitAsync(counterpartyId, EventType.DeliveryAddressUpdated, address.Id, address);

    public Task DeliveryAddressDeleted(Guid counterpartyId, Guid addressId)
        => EmitAsync(counterpartyId, EventType.DeliveryAddressDeleted, addressId, new { AddressId = addressId });

    public Task CargoReceiverCreated(Guid counterpartyId, UserCabinetCargoReceiverDto receiver)
        => EmitAsync(counterpartyId, EventType.ConsigneeCreated, receiver.Id, receiver);

    public Task CargoReceiverUpdated(Guid counterpartyId, UserCabinetCargoReceiverDto receiver)
        => EmitAsync(counterpartyId, EventType.ConsigneeUpdated, receiver.Id, receiver);

    public Task CargoReceiverDeleted(Guid counterpartyId, Guid receiverId)
        => EmitAsync(counterpartyId, EventType.ConsigneeDeleted, receiverId, new { ReceiverId = receiverId });

    public Task CartChanged(Guid counterpartyId, CartDto cart)
        => EmitAsync(counterpartyId, EventType.CartChanged, cart.Id, cart);

    public Task NotificationCreated(Guid counterpartyId, ShopNotificationDto notification)
        => EmitAsync(counterpartyId, EventType.NotificationCreated, notification.Id, notification);

    public Task NotificationUpdated(Guid counterpartyId, ShopNotificationDto notification)
        => EmitAsync(counterpartyId, EventType.NotificationUpdated, notification.Id, notification);

    public Task NotificationRemoved(Guid counterpartyId, Guid notificationId)
        => EmitAsync(counterpartyId, EventType.NotificationDeleted, notificationId, new { NotificationId = notificationId });

    public Task NotificationsReadAll(Guid counterpartyId)
        => EmitAsync(counterpartyId, EventType.NotificationRead, Guid.Empty, new { ReadAll = true });

    public Task UnreadNotificationsCountChanged(Guid counterpartyId, int count)
        => EmitAsync(counterpartyId, EventType.UnreadCountChanged, Guid.Empty, new { Count = count });
}
