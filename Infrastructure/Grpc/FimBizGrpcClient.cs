using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Infrastructure.Grpc;

public class FimBizGrpcClient : IFimBizGrpcClient
{
    public Task<Counterparty?> GetCounterpartyAsync(string phoneNumber)
    {
        // TODO: Implement gRPC call to FimBiz
        // Пока возвращаем null, так как gRPC интеграция будет реализована позже
        return Task.FromResult<Counterparty?>(null);
    }

    public Task<Counterparty?> GetCounterpartyByIdAsync(Guid counterpartyId)
    {
        // TODO: Implement gRPC call to FimBiz
        // Пока возвращаем null, так как gRPC интеграция будет реализована позже
        return Task.FromResult<Counterparty?>(null);
    }

    public Task<List<Discount>> GetCounterpartyDiscountsAsync(Guid counterpartyId)
    {
        // TODO: Implement gRPC call to FimBiz
        // Пока возвращаем пустой список
        return Task.FromResult(new List<Discount>());
    }

    public Task SyncCounterpartyAsync(Guid counterpartyId)
    {
        // TODO: Implement gRPC call to FimBiz
        return Task.CompletedTask;
    }
}

