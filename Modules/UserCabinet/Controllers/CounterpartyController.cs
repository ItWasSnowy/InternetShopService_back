using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Helpers;
using InternetShopService_back.Modules.UserCabinet.Services;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CounterpartyController : ControllerBase
{
    private readonly ICounterpartyService _counterpartyService;

    public CounterpartyController(ICounterpartyService counterpartyService)
    {
        _counterpartyService = counterpartyService;
    }

    [HttpGet("current")]
    public async Task<IActionResult> GetCurrentCounterparty()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var counterparty = await _counterpartyService.GetCurrentCounterpartyAsync(userId.Value);
            return Ok(counterparty);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpGet("discounts")]
    public async Task<IActionResult> GetDiscounts()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            // Получаем counterpartyId из текущего пользователя
            var counterpartyId = HttpContext.GetCounterpartyId();
            if (counterpartyId == null)
            {
                // Если нет в токене, получаем через сервис
                var counterparty = await _counterpartyService.GetCurrentCounterpartyAsync(userId.Value);
                counterpartyId = counterparty.Id;
            }

            var discounts = await _counterpartyService.GetDiscountsAsync(counterpartyId.Value);
            return Ok(discounts);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { error = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpPost("sync")]
    public async Task<IActionResult> SyncCounterpartyData()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            // Получаем counterpartyId из текущего пользователя
            var counterpartyId = HttpContext.GetCounterpartyId();
            if (counterpartyId == null)
            {
                var counterparty = await _counterpartyService.GetCurrentCounterpartyAsync(userId.Value);
                counterpartyId = counterparty.Id;
            }

            await _counterpartyService.SyncCounterpartyDataAsync(counterpartyId.Value);
            return Ok(new { message = "Данные синхронизированы успешно" });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }
}

