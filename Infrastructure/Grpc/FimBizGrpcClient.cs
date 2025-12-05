using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Infrastructure.Grpc;

public class FimBizGrpcClient : IFimBizGrpcClient
{
    public Task<Counterparty?> GetCounterpartyAsync(string phoneNumber)
    {
        // TODO: Implement gRPC call to FimBiz
        throw new NotImplementedException();
    }

    public Task<List<Discount>> GetCounterpartyDiscountsAsync(Guid counterpartyId)
    {
        // TODO: Implement gRPC call to FimBiz
        throw new NotImplementedException();
    }

    public Task SyncCounterpartyAsync(Guid counterpartyId)
    {
        // TODO: Implement gRPC call to FimBiz
        throw new NotImplementedException();
    }
}

