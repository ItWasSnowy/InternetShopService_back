using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Services;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

[ApiController]
[Route("api/[controller]")]
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
        // TODO: Get userId from JWT token
        var userId = Guid.NewGuid(); // Placeholder
        var counterparty = await _counterpartyService.GetCurrentCounterpartyAsync(userId);
        return Ok(counterparty);
    }

    [HttpGet("discounts")]
    public async Task<IActionResult> GetDiscounts()
    {
        // TODO: Get counterpartyId from current user
        var counterpartyId = Guid.NewGuid(); // Placeholder
        var discounts = await _counterpartyService.GetDiscountsAsync(counterpartyId);
        return Ok(discounts);
    }
}

