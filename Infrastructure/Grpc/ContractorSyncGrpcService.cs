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
    private readonly SessionControlService _sessionControlService;
    private readonly ILogger<ContractorSyncGrpcService> _logger;
    private readonly IConfiguration _configuration;

    public ContractorSyncGrpcService(
        FimBizSessionService fimBizSessionService,
        SessionControlService sessionControlService,
        ILogger<ContractorSyncGrpcService> logger,
        IConfiguration configuration)
    {
        _fimBizSessionService = fimBizSessionService;
        _sessionControlService = sessionControlService;
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
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ =====
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС GetActiveSessions ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.ContractorId: {ContractorId}", request?.ContractorId);
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

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

    /// <summary>
    /// Выполнить команду управления сессиями и получить результат
    /// </summary>
    public override async Task<ExecuteSessionControlResponse> ExecuteSessionControl(
        ExecuteSessionControlRequest request,
        ServerCallContext context)
    {
        // ===== ДИАГНОСТИЧЕСКОЕ ЛОГИРОВАНИЕ =====
        _logger.LogInformation("=== [CONTRACTOR] ВХОДЯЩИЙ ЗАПРОС ExecuteSessionControl ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        _logger.LogInformation("Request.SessionControl.ContractorId: {ContractorId}", request?.SessionControl?.ContractorId ?? 0);
        _logger.LogInformation("Request.SessionControl.Action: {Action}", request?.SessionControl?.Action);
        // ===== КОНЕЦ ДИАГНОСТИЧЕСКОГО ЛОГИРОВАНИЯ =====

        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при выполнении команды управления сессиями для контрагента {ContractorId}", 
                    request.SessionControl?.ContractorId ?? 0);
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            if (request.SessionControl == null)
            {
                _logger.LogWarning("Получен запрос ExecuteSessionControl без SessionControl");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "SessionControl is required"));
            }

            _logger.LogInformation("Выполнение команды управления сессиями для контрагента {ContractorId} от FimBiz", 
                request.SessionControl.ContractorId);

            var response = await _sessionControlService.ExecuteSessionControlAsync(
                request.SessionControl, 
                context.CancellationToken);

            _logger.LogInformation(
                "Результат выполнения команды для контрагента {ContractorId}: Success={Success}, Message={Message}, DisconnectedCount={DisconnectedCount}",
                request.SessionControl.ContractorId, 
                response.Success, 
                response.Message, 
                response.DisconnectedCount);

            return response;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при выполнении команды управления сессиями для контрагента {ContractorId}", 
                request.SessionControl?.ContractorId ?? 0);
            throw new RpcException(new Status(StatusCode.Internal, "Internal server error"));
        }
    }
}

