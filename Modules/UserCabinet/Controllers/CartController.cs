using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Helpers;
using InternetShopService_back.Modules.UserCabinet.Services;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
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
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var cart = await _cartService.GetCartAsync(userId.Value);
            return Ok(cart);
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

    [HttpPost("add")]
    public async Task<IActionResult> AddItem([FromBody] AddCartItemDto item)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var cart = await _cartService.AddItemAsync(userId.Value, item);
            return Ok(cart);
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

    [HttpPut("{itemId}")]
    public async Task<IActionResult> UpdateItem(Guid itemId, [FromBody] UpdateCartItemDto dto)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var cart = await _cartService.UpdateItemAsync(userId.Value, itemId, dto.Quantity);
            return Ok(cart);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpDelete("{itemId}")]
    public async Task<IActionResult> RemoveItem(Guid itemId)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var result = await _cartService.RemoveItemAsync(userId.Value, itemId);
            if (!result)
            {
                return NotFound(new { error = "Товар не найден в корзине" });
            }
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpDelete("clear")]
    public async Task<IActionResult> ClearCart()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var result = await _cartService.ClearCartAsync(userId.Value);
            if (!result)
            {
                return NotFound(new { error = "Корзина не найдена" });
            }
            return NoContent();
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }
}

