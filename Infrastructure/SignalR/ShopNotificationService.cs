using System;
using System.Linq;
using System.Threading.Tasks;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.Notifications.DTOs;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using UserCabinetDeliveryAddressDto = InternetShopService_back.Modules.UserCabinet.DTOs.DeliveryAddressDto;
using UserCabinetCargoReceiverDto = InternetShopService_back.Modules.UserCabinet.DTOs.CargoReceiverDto;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.SignalR;

public sealed class ShopNotificationService : IShopNotificationService
{
    private readonly IHubContext<ShopHub, IShopHubClient> _hubContext;
    private readonly ShopConnectionManager _connectionManager;
    private readonly ILogger<ShopNotificationService> _logger;

    public ShopNotificationService(
        IHubContext<ShopHub, IShopHubClient> hubContext,
        ShopConnectionManager connectionManager,
        ILogger<ShopNotificationService> logger)
    {
        _hubContext = hubContext;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    private Task Broadcast(Guid counterpartyId, Func<IShopHubClient, Task> send)
    {
        var recipients = _connectionManager.GetRecipientsByCounterpartyId(counterpartyId);
        if (!recipients.Any())
            return Task.CompletedTask;

        var tasks = recipients.Select(async r =>
        {
            try
            {
                await send(_hubContext.Clients.Client(r.ConnectionId));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SignalR send failed. CounterpartyId={CounterpartyId}, ConnectionId={ConnectionId}", counterpartyId, r.ConnectionId);
            }
        });

        return Task.WhenAll(tasks);
    }

    public Task OrderCreated(Guid counterpartyId, OrderDto order)
        => Broadcast(counterpartyId, c => c.OrderCreated(order));

    public Task OrderUpdated(Guid counterpartyId, OrderDto order)
        => Broadcast(counterpartyId, c => c.OrderUpdated(order));

    public Task OrderDeleted(Guid counterpartyId, Guid orderId)
        => Broadcast(counterpartyId, c => c.OrderDeleted(orderId));

    public Task OrderCommentAdded(Guid counterpartyId, OrderCommentDto comment)
        => Broadcast(counterpartyId, c => c.OrderCommentAdded(comment));

    public Task CounterpartyUpdated(Guid counterpartyId, CounterpartyDto counterparty)
        => Broadcast(counterpartyId, c => c.CounterpartyUpdated(counterparty));

    public Task DeliveryAddressCreated(Guid counterpartyId, UserCabinetDeliveryAddressDto address)
        => Broadcast(counterpartyId, c => c.DeliveryAddressCreated(address));

    public Task DeliveryAddressUpdated(Guid counterpartyId, UserCabinetDeliveryAddressDto address)
        => Broadcast(counterpartyId, c => c.DeliveryAddressUpdated(address));

    public Task DeliveryAddressDeleted(Guid counterpartyId, Guid addressId)
        => Broadcast(counterpartyId, c => c.DeliveryAddressDeleted(addressId));

    public Task CargoReceiverCreated(Guid counterpartyId, UserCabinetCargoReceiverDto receiver)
        => Broadcast(counterpartyId, c => c.CargoReceiverCreated(receiver));

    public Task CargoReceiverUpdated(Guid counterpartyId, UserCabinetCargoReceiverDto receiver)
        => Broadcast(counterpartyId, c => c.CargoReceiverUpdated(receiver));

    public Task CargoReceiverDeleted(Guid counterpartyId, Guid receiverId)
        => Broadcast(counterpartyId, c => c.CargoReceiverDeleted(receiverId));

    public Task CartChanged(Guid counterpartyId, CartDto cart)
        => Broadcast(counterpartyId, c => c.CartChanged(cart));

    public Task NotificationCreated(Guid counterpartyId, ShopNotificationDto notification)
        => Broadcast(counterpartyId, c => c.NotificationCreated(notification));

    public Task NotificationUpdated(Guid counterpartyId, ShopNotificationDto notification)
        => Broadcast(counterpartyId, c => c.NotificationUpdated(notification));

    public Task NotificationRemoved(Guid counterpartyId, Guid notificationId)
        => Broadcast(counterpartyId, c => c.NotificationRemoved(notificationId));

    public Task NotificationsReadAll(Guid counterpartyId)
        => Broadcast(counterpartyId, c => c.NotificationsReadAll());

    public Task UnreadNotificationsCountChanged(Guid counterpartyId, int count)
        => Broadcast(counterpartyId, c => c.UnreadNotificationsCountChanged(count));
}
