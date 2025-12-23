using System;
using System.Security.Claims;
using System.Threading.Tasks;
using InternetShopService_back.Data;
using InternetShopService_back.Modules.Notifications.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.SignalR;

[Authorize]
public class ShopHub : Hub<IShopHubClient>
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ShopConnectionManager _connectionManager;
    private readonly ILogger<ShopHub> _logger;

    public ShopHub(ApplicationDbContext dbContext, ShopConnectionManager connectionManager, ILogger<ShopHub> logger)
    {
        _dbContext = dbContext;
        _connectionManager = connectionManager;
        _logger = logger;
    }

    public async Task JoinHub()
    {
        var connectionId = Context.ConnectionId;
        var userIdStr = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var counterpartyIdStr = Context.User?.FindFirstValue("CounterpartyId");

        if (string.IsNullOrWhiteSpace(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
        {
            _logger.LogWarning("ShopHub.JoinHub called without valid user id. ConnectionId={ConnectionId}", connectionId);
            await Clients.Caller.Disconnected("Ошибка авторизации");
            await Task.Delay(200);
            return;
        }

        if (string.IsNullOrWhiteSpace(counterpartyIdStr) || !Guid.TryParse(counterpartyIdStr, out var counterpartyId))
        {
            _logger.LogWarning("ShopHub.JoinHub called without valid CounterpartyId. ConnectionId={ConnectionId}, UserId={UserId}", connectionId, userId);
            await Clients.Caller.Disconnected("Ошибка авторизации (CounterpartyId)");
            await Task.Delay(200);
            return;
        }

        _connectionManager.AddConnection(connectionId, userId, counterpartyId);
        _logger.LogInformation("ShopHub joined. UserId={UserId}, CounterpartyId={CounterpartyId}, ConnectionId={ConnectionId}", userId, counterpartyId, connectionId);

        await Clients.Caller.ConnectionConfirmed("Успешно подключен к ShopHub");

        var unreadCount = await _dbContext.ShopNotifications
            .AsNoTracking()
            .Where(n => n.CounterpartyId == counterpartyId)
            .Where(n => n.DeletedAt == null)
            .Where(n => n.UserAccountId == null || n.UserAccountId == userId)
            .Where(n => !n.IsRead)
            .CountAsync();

        var unreadItems = await _dbContext.ShopNotifications
            .AsNoTracking()
            .Where(n => n.CounterpartyId == counterpartyId)
            .Where(n => n.DeletedAt == null)
            .Where(n => n.UserAccountId == null || n.UserAccountId == userId)
            .Where(n => !n.IsRead)
            .OrderBy(n => n.CreatedAt)
            .Take(100)
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

        foreach (var n in unreadItems)
        {
            await Clients.Caller.NotificationCreated(n);
        }

        await Clients.Caller.UnreadNotificationsCountChanged(unreadCount);
    }

    public Task LeaveHub()
    {
        var connectionId = Context.ConnectionId;
        _connectionManager.RemoveConnection(connectionId);
        _logger.LogInformation("ShopHub left. ConnectionId={ConnectionId}", connectionId);
        return Task.CompletedTask;
    }

    public Task Ping()
    {
        _connectionManager.UpdateActivity(Context.ConnectionId);
        return Task.CompletedTask;
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _connectionManager.RemoveConnection(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
