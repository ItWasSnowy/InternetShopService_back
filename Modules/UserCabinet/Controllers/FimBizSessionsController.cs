using InternetShopService_back.Infrastructure.Grpc.Contractors;
using InternetShopService_back.Modules.UserCabinet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

/// <summary>
/// Контроллер для получения информации о сессиях для FimBiz
/// </summary>
[ApiController]
[Route("api/fimbiz/sessions")]
public class FimBizSessionsController : ControllerBase
{
    private readonly FimBizSessionService _fimBizSessionService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FimBizSessionsController> _logger;

    public FimBizSessionsController(
        FimBizSessionService fimBizSessionService,
        IConfiguration configuration,
        ILogger<FimBizSessionsController> logger)
    {
        _fimBizSessionService = fimBizSessionService;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Получить активные сессии контрагента по FimBiz Contractor ID
    /// </summary>
    /// <param name="contractorId">ID контрагента в FimBiz</param>
    /// <returns>Список сессий контрагента</returns>
    [HttpGet("contractor/{contractorId}")]
    public async Task<ActionResult<GetActiveSessionsResponse>> GetActiveSessions(int contractorId)
    {
        try
        {
            // Проверка API ключа для безопасности
            var apiKey = Request.Headers["X-API-Key"].ToString();
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при запросе сессий для контрагента {ContractorId}", contractorId);
                return Unauthorized("Неверный API ключ");
            }

            var response = await _fimBizSessionService.GetActiveSessionsByContractorIdAsync(contractorId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении сессий для контрагента {ContractorId}", contractorId);
            return StatusCode(500, "Ошибка при получении сессий");
        }
    }
}

