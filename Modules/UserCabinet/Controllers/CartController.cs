using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Services;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CartController : ControllerBase
{
    private readonly ICartService _cartService;

    public CartController(ICartService cartService)
    {
        _cartService = cartService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCart()
    {
        // TODO: Get userId from JWT token
        var userId = Guid.NewGuid(); // Placeholder
        var cart = await _cartService.GetCartAsync(userId);
        return Ok(cart);
    }

    [HttpPost("add")]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemDto item)
    {
        // TODO: Get userId from JWT token
        var userId = Guid.NewGuid(); // Placeholder
        var cart = await _cartService.AddItemAsync(userId, item);
        return Ok(cart);
    }

    [HttpPut("{itemId}")]
    public async Task<IActionResult> UpdateItem(Guid itemId, [FromBody] UpdateCartItemDto dto)
    {
        // TODO: Get userId from JWT token
        var userId = Guid.NewGuid(); // Placeholder
        var cart = await _cartService.UpdateItemAsync(userId, itemId, dto.Quantity);
        return Ok(cart);
    }

    [HttpDelete("{itemId}")]
    public async Task<IActionResult> RemoveItem(Guid itemId)
    {
        // TODO: Get userId from JWT token
        var userId = Guid.NewGuid(); // Placeholder
        await _cartService.RemoveItemAsync(userId, itemId);
        return NoContent();
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> ClearCart()
    {
        // TODO: Get userId from JWT token
        var userId = Guid.NewGuid(); // Placeholder
        await _cartService.ClearCartAsync(userId);
        return NoContent();
    }
}

