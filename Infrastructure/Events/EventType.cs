namespace InternetShopService_back.Infrastructure.Events;

public enum EventType
{
    NotificationCreated,
    NotificationUpdated,
    NotificationDeleted,
    NotificationRead,
    UnreadCountChanged,

    CartChanged,

    ConsigneeCreated,
    ConsigneeUpdated,
    ConsigneeDeleted,

    DeliveryAddressCreated,
    DeliveryAddressUpdated,
    DeliveryAddressDeleted,

    CounterpartyCreated,
    CounterpartyUpdated,
    CounterpartyDeleted,

    OrderCreated,
    OrderUpdated,
    OrderCommentAdded
}
