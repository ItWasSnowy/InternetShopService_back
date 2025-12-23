using System.Collections.Generic;
using System.Threading.Tasks;

namespace InternetShopService_back.Infrastructure.Events;

public interface IEventStore
{
    Task<long> StoreEventAsync(UnifiedEvent @event);
    Task<List<UnifiedEvent>> GetEventsSinceAsync(long sequenceNumber, string userId);
    Task<long> GetLatestSequenceNumberAsync();
}
