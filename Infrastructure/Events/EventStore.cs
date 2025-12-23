using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Events.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Infrastructure.Events;

public sealed class EventStore : IEventStore
{
    private readonly ApplicationDbContext _db;

    public EventStore(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<long> StoreEventAsync(UnifiedEvent @event)
    {
        var entity = new OrderEvent
        {
            UserId = @event.UserId,
            EventType = (int)@event.EventType,
            EntityId = @event.EntityId,
            Data = @event.Data,
            CreatedAt = @event.Timestamp == default ? DateTime.UtcNow : @event.Timestamp
        };

        _db.OrderEvents.Add(entity);
        await _db.SaveChangesAsync();

        @event.SequenceNumber = entity.SequenceNumber;
        @event.Timestamp = entity.CreatedAt;
        return entity.SequenceNumber;
    }

    public async Task<List<UnifiedEvent>> GetEventsSinceAsync(long sequenceNumber, string userId)
    {
        var rows = await _db.OrderEvents
            .AsNoTracking()
            .Where(e => e.UserId == userId)
            .Where(e => e.SequenceNumber > sequenceNumber)
            .OrderBy(e => e.SequenceNumber)
            .Take(100)
            .ToListAsync();

        return rows.Select(e => new UnifiedEvent
        {
            SequenceNumber = e.SequenceNumber,
            UserId = e.UserId,
            EventType = (EventType)e.EventType,
            EntityId = e.EntityId,
            Timestamp = e.CreatedAt,
            Data = e.Data
        }).ToList();
    }

    public async Task<long> GetLatestSequenceNumberAsync()
    {
        return await _db.OrderEvents.AsNoTracking().Select(e => (long?)e.SequenceNumber).MaxAsync() ?? 0;
    }
}
