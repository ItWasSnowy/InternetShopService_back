using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using InternetShopService_back.Infrastructure.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Events;

public sealed class EventNotificationService
{
    private readonly IEventStore _eventStore;
    private readonly IHubContext<ShopHub, IShopHubClient> _hubContext;
    private readonly ILogger<EventNotificationService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public EventNotificationService(IEventStore eventStore, IHubContext<ShopHub, IShopHubClient> hubContext, ILogger<EventNotificationService> logger)
    {
        _eventStore = eventStore;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task NotifyEventAsync(string userId, EventType eventType, Guid entityId, object data)
    {
        var json = JsonSerializer.Serialize(data, JsonOptions);

        var evt = new UnifiedEvent
        {
            SequenceNumber = 0,
            UserId = userId,
            EventType = eventType,
            EntityId = entityId,
            Timestamp = DateTime.UtcNow,
            Data = json
        };

        var seq = await _eventStore.StoreEventAsync(evt);

        _logger.LogDebug("Stored event. Seq={Seq}, UserId={UserId}, Type={Type}, EntityId={EntityId}", seq, userId, eventType, entityId);

        await _hubContext.Clients.User(userId).ReceiveEvent(evt);

        _logger.LogDebug("Pushed event to SignalR. Seq={Seq}, UserId={UserId}", seq, userId);
    }
}
