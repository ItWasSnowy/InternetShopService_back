using Grpc.Core;
using Grpc.Core.Interceptors;
using InternetShopService_back.Shared.Repositories;
using InternetShopService_back.Shared.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Grpc.Interceptors;

public class FimBizApiKeyInterceptor : Interceptor
{
    private readonly ILogger<FimBizApiKeyInterceptor> _logger;
    private readonly IConfiguration _configuration;
    private readonly IShopApiKeyRepository _shopApiKeyRepository;
    private readonly IShopRepository _shopRepository;
    private readonly IShopContext _shopContext;

    public FimBizApiKeyInterceptor(
        ILogger<FimBizApiKeyInterceptor> logger,
        IConfiguration configuration,
        IShopApiKeyRepository shopApiKeyRepository,
        IShopRepository shopRepository,
        IShopContext shopContext)
    {
        _logger = logger;
        _configuration = configuration;
        _shopApiKeyRepository = shopApiKeyRepository;
        _shopRepository = shopRepository;
        _shopContext = shopContext;
    }

    public override async Task<TResponse> UnaryServerHandler<TRequest, TResponse>(
        TRequest request,
        ServerCallContext context,
        UnaryServerMethod<TRequest, TResponse> continuation)
    {
        await AuthorizeAsync(context);
        return await continuation(request, context);
    }

    public override async Task<TResponse> ClientStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        ServerCallContext context,
        ClientStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthorizeAsync(context);
        return await continuation(requestStream, context);
    }

    public override async Task ServerStreamingServerHandler<TRequest, TResponse>(
        TRequest request,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        ServerStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthorizeAsync(context);
        await continuation(request, responseStream, context);
    }

    public override async Task DuplexStreamingServerHandler<TRequest, TResponse>(
        IAsyncStreamReader<TRequest> requestStream,
        IServerStreamWriter<TResponse> responseStream,
        ServerCallContext context,
        DuplexStreamingServerMethod<TRequest, TResponse> continuation)
    {
        await AuthorizeAsync(context);
        await continuation(requestStream, responseStream, context);
    }

    private async Task AuthorizeAsync(ServerCallContext context)
    {
        var apiKey = context.RequestHeaders.GetValue("x-api-key");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new RpcException(new Status(StatusCode.Unauthenticated, "Missing API key"));
        }

        var keyRecord = await _shopApiKeyRepository.GetActiveByApiKeyAsync(apiKey);
        if (keyRecord != null)
        {
            _shopContext.SetShopId(keyRecord.ShopId);
            return;
        }

        var fallbackApiKey = _configuration["FimBiz:ApiKey"];
        if (!string.IsNullOrWhiteSpace(fallbackApiKey) && apiKey == fallbackApiKey)
        {
            var defaultShop = await _shopRepository.GetByFimBizCompanyIdAsync(0);
            if (defaultShop != null)
            {
                _shopContext.SetShopId(defaultShop.Id);
            }
            return;
        }

        _logger.LogWarning("Неверный API ключ для входящего gRPC вызова {Method}. Peer: {Peer}", context.Method, context.Peer);
        throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
    }
}
