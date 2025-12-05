using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public interface ICounterpartyService
{
    Task<CounterpartyDto> GetCurrentCounterpartyAsync(Guid userId);
    Task<List<DiscountDto>> GetDiscountsAsync(Guid counterpartyId);
    Task SyncCounterpartyDataAsync(Guid counterpartyId);
}

