using Grpc.Core;
using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Modules.UserCabinet.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Grpc;

/// <summary>
/// gRPC сервис для обработки запросов от FimBiz
/// </summary>
public class ContractorSyncGrpcService : ContractorSyncService.ContractorSyncServiceBase
{
    private readonly FimBizSessionService _fimBizSessionService;
    private readonly ILogger<ContractorSyncGrpcService> _logger;
    private readonly IConfiguration _configuration;

    public ContractorSyncGrpcService(
        FimBizSessionService fimBizSessionService,
        ILogger<ContractorSyncGrpcService> logger,
        IConfiguration configuration)
    {
        _fimBizSessionService = fimBizSessionService;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Получить список активных сессий контрагента
    /// </summary>
    public override async Task<GetActiveSessionsResponse> GetActiveSessions(
        GetActiveSessionsRequest request,
        ServerCallContext context)
    {
        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при запросе сессий для контрагента {ContractorId}", 
                    request.ContractorId);
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            _logger.LogInformation("Запрос сессий для контрагента {ContractorId} от FimBiz", request.ContractorId);

            var response = await _fimBizSessionService.GetActiveSessionsByContractorIdAsync(request.ContractorId);
            
            _logger.LogInformation("Возвращено {Count} сессий для контрагента {ContractorId}", 
                response.Sessions.Count, request.ContractorId);

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении сессий для контрагента {ContractorId}", request.ContractorId);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }
}

