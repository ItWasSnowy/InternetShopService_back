using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Infrastructure.Grpc;

public interface IFimBizGrpcClient
{
    Task<Counterparty?> GetCounterpartyAsync(string phoneNumber);
    Task<Counterparty?> GetCounterpartyByIdAsync(Guid counterpartyId);
    Task<List<Discount>> GetCounterpartyDiscountsAsync(Guid counterpartyId);
    Task SyncCounterpartyAsync(Guid counterpartyId);
}

