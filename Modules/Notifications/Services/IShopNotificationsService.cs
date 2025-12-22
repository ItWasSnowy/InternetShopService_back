using System;
using System.Threading.Tasks;
using InternetShopService_back.Modules.Notifications.DTOs;
using InternetShopService_back.Modules.Notifications.Models;

namespace InternetShopService_back.Modules.Notifications.Services;

public interface IShopNotificationsService
{
    Task<PagedNotificationsDto> GetNotificationsAsync(Guid userId, Guid counterpartyId, int page, int pageSize);
    Task<int> GetUnreadCountAsync(Guid userId, Guid counterpartyId);

    Task<ShopNotificationDto?> MarkAsReadAsync(Guid notificationId, Guid userId, Guid counterpartyId);
    Task<int> MarkAllAsReadAsync(Guid userId, Guid counterpartyId);

    Task<bool> DeleteAsync(Guid notificationId, Guid userId, Guid counterpartyId);

    Task<ShopNotificationDto> CreateAsync(
        Guid counterpartyId,
        Guid? userAccountId,
        string title,
        string? description,
        ShopNotificationObjectType objectType,
        Guid objectId);
}
