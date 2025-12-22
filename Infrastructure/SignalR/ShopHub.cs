using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.SignalR;

[Authorize]
public class ShopHub : Hub<IShopHubClient>
{
    private readonly ShopConnectionManager _connectionManager;
    private readonly ILogger<ShopHub> _logger;

    public ShopHub(ShopConnectionManager connectionManager, ILogger<ShopHub> logger)
    {
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
