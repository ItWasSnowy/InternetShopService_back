using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace InternetShopService_back.Infrastructure.SignalR;

public sealed class ShopConnectionManager
{
    private static readonly ConcurrentDictionary<string, ConnectedShopUser> _connections = new();

    public bool AddConnection(string connectionId, Guid userId, Guid counterpartyId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return false;

        _connections[connectionId] = new ConnectedShopUser
        {
            ConnectionId = connectionId,
            UserId = userId,
            CounterpartyId = counterpartyId,
            ConnectedAt = DateTime.UtcNow,
            LastActivity = DateTime.UtcNow
        };

        return true;
    }

    public void RemoveConnection(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        _connections.TryRemove(connectionId, out _);
    }

    public void UpdateActivity(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return;
        if (_connections.TryGetValue(connectionId, out var user))
        {
            user.LastActivity = DateTime.UtcNow;
        }
    }

    public ConnectedShopUser? GetByConnectionId(string connectionId)
    {
        if (string.IsNullOrWhiteSpace(connectionId)) return null;
        return _connections.TryGetValue(connectionId, out var user) ? user : null;
    }

    public List<ConnectedShopUser> GetRecipientsByCounterpartyId(Guid counterpartyId)
    {
        return _connections.Values
            .Where(c => c.CounterpartyId == counterpartyId)
            .ToList();
    }

    public List<ConnectedShopUser> GetAllConnections() => _connections.Values.ToList();

    public sealed class ConnectedShopUser
    {
        public required string ConnectionId { get; init; }
        public required Guid UserId { get; init; }
        public required Guid CounterpartyId { get; init; }
        public DateTime ConnectedAt { get; init; }
        public DateTime LastActivity { get; set; }
    }
}
