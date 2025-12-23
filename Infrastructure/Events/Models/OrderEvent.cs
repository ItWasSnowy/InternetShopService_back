using System;

namespace InternetShopService_back.Infrastructure.Events.Models;

public sealed class OrderEvent
{
    public long SequenceNumber { get; set; }
    public required string UserId { get; set; }
    public int EventType { get; set; }
    public Guid EntityId { get; set; }
    public required string Data { get; set; }
    public DateTime CreatedAt { get; set; }
}
