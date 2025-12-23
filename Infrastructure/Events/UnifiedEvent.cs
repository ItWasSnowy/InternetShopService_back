using System;

namespace InternetShopService_back.Infrastructure.Events;

public sealed class UnifiedEvent
{
    public long SequenceNumber { get; set; }
    public required string UserId { get; set; }
    public EventType EventType { get; set; }
    public Guid EntityId { get; set; }
    public DateTime Timestamp { get; set; }

    // JSON payload; object-polymorphism with System.Text.Json is fragile,
    // so we ship the stored JSON as-is.
    public required string Data { get; set; }
}
