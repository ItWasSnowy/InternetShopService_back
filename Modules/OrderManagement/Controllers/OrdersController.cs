using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Services;
using InternetShopService_back.Modules.UserCabinet.Helpers;

namespace InternetShopService_back.Modules.OrderManagement.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet]
    public async Task<IActionResult> GetOrders([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var result = await _orderService.GetOrdersByUserPagedAsync(userId.Value, page, pageSize);
            return Ok(result);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetOrder(Guid id)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var order = await _orderService.GetOrderAsync(id);
            if (order == null)
                return NotFound(new { error = "Заказ не найден" });

            // Проверяем, что заказ принадлежит пользователю
            // TODO: Добавить проверку в OrderService или здесь
            return Ok(order);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateOrder([FromBody] CreateOrderDto dto)
    {
        try
        {
            var order = await _orderService.CreateOrderAsync(dto);
            return CreatedAtAction(nameof(GetOrder), new { id = order.Id }, order);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateOrderStatus(Guid id, [FromBody] UpdateOrderStatusDto dto)
    {
        try
        {
            var order = await _orderService.UpdateOrderStatusAsync(id, dto.Status);
            return Ok(order);
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}

