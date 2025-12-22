using System;
using InternetShopService_back.Modules.Notifications.Models;

namespace InternetShopService_back.Modules.Notifications.DTOs;

public class ShopNotificationDto
{
    public Guid Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }

    public ShopNotificationObjectType ObjectType { get; set; }
    public Guid ObjectId { get; set; }

    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }

    public DateTime CreatedAt { get; set; }
}

public class UnreadNotificationsCountDto
{
    public int Count { get; set; }
}
