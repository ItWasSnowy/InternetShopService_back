using System;
using System.Linq;
using System.Threading.Tasks;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.SignalR;
using InternetShopService_back.Modules.Notifications.DTOs;
using InternetShopService_back.Modules.Notifications.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.Notifications.Services;

public sealed class ShopNotificationsService : IShopNotificationsService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IShopNotificationService _shopSignalR;

    public ShopNotificationsService(ApplicationDbContext dbContext, IShopNotificationService shopSignalR)
    {
        _dbContext = dbContext;
        _shopSignalR = shopSignalR;
    }

    private static IQueryable<ShopNotification> VisibleToUser(IQueryable<ShopNotification> query, Guid userId, Guid counterpartyId)
    {
        return query
            .Where(n => n.CounterpartyId == counterpartyId)
            .Where(n => n.DeletedAt == null)
            .Where(n => n.UserAccountId == null || n.UserAccountId == userId);
    }

    public async Task<PagedNotificationsDto> GetNotificationsAsync(Guid userId, Guid counterpartyId, int page, int pageSize)
    {
        if (page < 1) page = 1;
        if (pageSize < 1) pageSize = 20;
        if (pageSize > 100) pageSize = 100;

        var baseQuery = VisibleToUser(_dbContext.ShopNotifications.AsNoTracking(), userId, counterpartyId);

        var total = await baseQuery.CountAsync();

        var items = await baseQuery
            .OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(n => new ShopNotificationDto
            {
                Id = n.Id,
                Title = n.Title,
                Description = n.Description,
                ObjectType = n.ObjectType,
                ObjectId = n.ObjectId,
                IsRead = n.IsRead,
                ReadAt = n.ReadAt,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return new PagedNotificationsDto
        {
            Items = items,
            TotalCount = total,
            Page = page,
            PageSize = pageSize
        };
    }

    public Task<int> GetUnreadCountAsync(Guid userId, Guid counterpartyId)
    {
        return VisibleToUser(_dbContext.ShopNotifications.AsNoTracking(), userId, counterpartyId)
            .Where(n => !n.IsRead)
            .CountAsync();
    }

    public async Task<ShopNotificationDto?> MarkAsReadAsync(Guid notificationId, Guid userId, Guid counterpartyId)
    {
        var notification = await VisibleToUser(_dbContext.ShopNotifications, userId, counterpartyId)
            .FirstOrDefaultAsync(n => n.Id == notificationId);

        if (notification == null)
            return null;

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            notification.ReadAt = DateTime.UtcNow;
            notification.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var dto = Map(notification);
            var unread = await GetUnreadCountAsync(userId, counterpartyId);
            await _shopSignalR.NotificationUpdated(counterpartyId, dto);
            await _shopSignalR.UnreadNotificationsCountChanged(counterpartyId, unread);

            return dto;
        }

        return Map(notification);
    }

    public async Task<int> MarkAllAsReadAsync(Guid userId, Guid counterpartyId)
    {
        var now = DateTime.UtcNow;

        var toUpdate = await VisibleToUser(_dbContext.ShopNotifications, userId, counterpartyId)
            .Where(n => !n.IsRead)
            .ToListAsync();

        if (!toUpdate.Any())
            return 0;

        foreach (var n in toUpdate)
        {
            n.IsRead = true;
            n.ReadAt = now;
            n.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync();

        await _shopSignalR.NotificationsReadAll(counterpartyId);
        await _shopSignalR.UnreadNotificationsCountChanged(counterpartyId, 0);

        return toUpdate.Count;
    }

    public async Task<bool> DeleteAsync(Guid notificationId, Guid userId, Guid counterpartyId)
    {
        var notification = await VisibleToUser(_dbContext.ShopNotifications, userId, counterpartyId)
            .FirstOrDefaultAsync(n => n.Id == notificationId);

        if (notification == null)
            return false;

        if (notification.DeletedAt == null)
        {
            notification.DeletedAt = DateTime.UtcNow;
            notification.UpdatedAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            var unread = await GetUnreadCountAsync(userId, counterpartyId);
            await _shopSignalR.NotificationRemoved(counterpartyId, notificationId);
            await _shopSignalR.UnreadNotificationsCountChanged(counterpartyId, unread);
        }

        return true;
    }

    public async Task<ShopNotificationDto> CreateAsync(
        Guid counterpartyId,
        Guid? userAccountId,
        string title,
        string? description,
        ShopNotificationObjectType objectType,
        Guid objectId)
    {
        var now = DateTime.UtcNow;

        var entity = new ShopNotification
        {
            Id = Guid.NewGuid(),
            CounterpartyId = counterpartyId,
            UserAccountId = userAccountId,
            Title = title,
            Description = description,
            ObjectType = objectType,
            ObjectId = objectId,
            IsRead = false,
            ReadAt = null,
            CreatedAt = now,
            UpdatedAt = now,
            DeletedAt = null
        };

        _dbContext.ShopNotifications.Add(entity);
        await _dbContext.SaveChangesAsync();

        var dto = Map(entity);

        // unread count depends on specific user; if userAccountId is null we can broadcast count based on counterparty-wide,
        // but for now we broadcast event and let client refetch count; additionally push count for the target user (if specified)
        // by sending count per counterparty (clients under same counterparty will receive).
        var unread = userAccountId.HasValue
            ? await GetUnreadCountAsync(userAccountId.Value, counterpartyId)
            : await _dbContext.ShopNotifications.AsNoTracking().Where(n => n.CounterpartyId == counterpartyId && n.DeletedAt == null && !n.IsRead).CountAsync();

        await _shopSignalR.NotificationCreated(counterpartyId, dto);
        await _shopSignalR.UnreadNotificationsCountChanged(counterpartyId, unread);

        return dto;
    }

    private static ShopNotificationDto Map(ShopNotification n)
    {
        return new ShopNotificationDto
        {
            Id = n.Id,
            Title = n.Title,
            Description = n.Description,
            ObjectType = n.ObjectType,
            ObjectId = n.ObjectId,
            IsRead = n.IsRead,
            ReadAt = n.ReadAt,
            CreatedAt = n.CreatedAt
        };
    }
}
