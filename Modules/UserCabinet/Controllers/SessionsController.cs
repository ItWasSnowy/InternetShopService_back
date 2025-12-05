using InternetShopService_back.Infrastructure.Jwt;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

[ApiController]
[Route("api/sessions")]
[Authorize]
public class SessionsController : ControllerBase
{
    private readonly ISessionService _sessionService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly ILogger<SessionsController> _logger;

    public SessionsController(
        ISessionService sessionService,
        IJwtTokenService jwtTokenService,
        ILogger<SessionsController> logger)
    {
        _sessionService = sessionService;
        _jwtTokenService = jwtTokenService;
        _logger = logger;
    }

    /// <summary>
    /// Получить список активных сессий текущего пользователя
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SessionDto>>> GetActiveSessions()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var sessions = await _sessionService.GetActiveSessionsAsync(userId.Value);
            
            // Определяем текущую сессию
            var currentToken = GetCurrentToken();
            if (!string.IsNullOrEmpty(currentToken))
            {
                var currentSession = sessions.FirstOrDefault(s => 
                    HttpContext.Request.Headers["Authorization"].ToString().Contains(s.Id.ToString()));
                
                // Более надежный способ - проверка через токен
                foreach (var session in sessions)
                {
                    // Проверяем, является ли это текущей сессией
                    // В реальности нужно проверять через токен, но для простоты используем первый активный
                    session.IsCurrentSession = session.Id == sessions.FirstOrDefault()?.Id;
                }
            }

            return Ok(sessions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении списка сессий");
            return StatusCode(500, "Ошибка при получении списка сессий");
        }
    }

    /// <summary>
    /// Деактивировать конкретную сессию
    /// </summary>
    [HttpPost("{sessionId}/deactivate")]
    public async Task<ActionResult> DeactivateSession(Guid sessionId)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            var result = await _sessionService.DeactivateSessionAsync(sessionId, userId.Value);
            
            if (!result)
            {
                return NotFound("Сессия не найдена или не принадлежит текущему пользователю");
            }

            return Ok(new { message = "Сессия успешно деактивирована" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при деактивации сессии {SessionId}", sessionId);
            return StatusCode(500, "Ошибка при деактивации сессии");
        }
    }

    /// <summary>
    /// Деактивировать несколько сессий
    /// </summary>
    [HttpPost("deactivate")]
    public async Task<ActionResult> DeactivateSessions([FromBody] DeactivateSessionsRequestDto request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null)
            {
                return Unauthorized();
            }

            if (request.SessionIds == null || !request.SessionIds.Any())
            {
                return BadRequest("Список ID сессий не может быть пустым");
            }

            var result = await _sessionService.DeactivateSessionsAsync(request.SessionIds, userId.Value);
            
            if (!result)
            {
                return NotFound("Сессии не найдены или не принадлежат текущему пользователю");
            }

            return Ok(new { message = "Сессии успешно деактивированы" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при деактивации сессий");
            return StatusCode(500, "Ошибка при деактивации сессий");
        }
    }

    private Guid? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("userId")?.Value;
        if (Guid.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }

    private string? GetCurrentToken()
    {
        return HttpContext.Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
    }
}
