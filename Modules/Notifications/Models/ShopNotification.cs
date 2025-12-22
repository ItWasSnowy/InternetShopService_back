using System;

namespace InternetShopService_back.Modules.Notifications.Models;

public class ShopNotification
{
    public Guid Id { get; set; }

    public Guid CounterpartyId { get; set; }

    public Guid? UserAccountId { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public ShopNotificationObjectType ObjectType { get; set; }

    public Guid ObjectId { get; set; }

    public bool IsRead { get; set; }

    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
