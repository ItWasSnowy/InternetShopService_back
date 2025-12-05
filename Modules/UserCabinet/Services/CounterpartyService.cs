using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class CounterpartyService : ICounterpartyService
{
    public Task<CounterpartyDto> GetCurrentCounterpartyAsync(Guid userId)
    {
        throw new NotImplementedException();
    }

    public Task<List<DiscountDto>> GetDiscountsAsync(Guid counterpartyId)
    {
        throw new NotImplementedException();
    }

    public Task SyncCounterpartyDataAsync(Guid counterpartyId)
    {
        throw new NotImplementedException();
    }
}

