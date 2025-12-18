using Grpc.Core;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Infrastructure.Grpc;

public interface IFimBizGrpcClient
{
    Task<Counterparty?> GetCounterpartyAsync(string phoneNumber);
    Task<Counterparty?> GetCounterpartyByIdAsync(Guid counterpartyId);
    Task<Counterparty?> GetCounterpartyByFimBizIdAsync(int fimBizContractorId);
    Task<List<Discount>> GetCounterpartyDiscountsAsync(int fimBizContractorId);
    Task SyncCounterpartyAsync(Guid counterpartyId);
    
    // Методы для синхронизации
    Task<GetContractorsResponse> GetContractorsAsync(GetContractorsRequest request);
    Task<Contractor?> GetContractorGrpcAsync(int fimBizContractorId);
    AsyncServerStreamingCall<ContractorChange> SubscribeToChanges(SubscribeRequest request);
    
    // Метод для получения активных сессий контрагента
    Task<GetActiveSessionsResponse> GetActiveSessionsAsync(GetActiveSessionsRequest request);
    
    // Методы для работы с заказами
    Task<CreateOrderResponse> CreateOrderAsync(CreateOrderRequest request);
    Task<UpdateOrderStatusResponse> UpdateOrderStatusAsync(UpdateOrderStatusRequest request);
    Task<Order> GetOrderAsync(GetOrderRequest request);
    
    // Методы для работы с комментариями к заказам
    Task<CreateCommentResponse> CreateCommentAsync(CreateCommentRequest request);
    Task<GetOrderCommentsResponse> GetOrderCommentsAsync(GetOrderCommentsRequest request);
}

