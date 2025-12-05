using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Helpers;
using InternetShopService_back.Modules.UserCabinet.Services;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CargoReceiverController : ControllerBase
{
    private readonly ICargoReceiverService _receiverService;

    public CargoReceiverController(ICargoReceiverService receiverService)
    {
        _receiverService = receiverService;
    }

    [HttpGet]
    public async Task<IActionResult> GetReceivers()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var receivers = await _receiverService.GetReceiversAsync(userId.Value);
            return Ok(receivers);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetReceiver(Guid id)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var receiver = await _receiverService.GetReceiverAsync(userId.Value, id);
            if (receiver == null)
                return NotFound(new { error = "Грузополучатель не найден" });

            return Ok(receiver);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpGet("default")]
    public async Task<IActionResult> GetDefaultReceiver()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var receiver = await _receiverService.GetDefaultReceiverAsync(userId.Value);
            if (receiver == null)
                return NotFound(new { error = "Грузополучатель по умолчанию не найден" });

            return Ok(receiver);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateReceiver([FromBody] CreateCargoReceiverDto dto)
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
            var receiver = await _receiverService.CreateReceiverAsync(userId.Value, dto);
            return CreatedAtAction(nameof(GetReceiver), new { id = receiver.Id }, receiver);
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

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateReceiver(Guid id, [FromBody] UpdateCargoReceiverDto dto)
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
            var receiver = await _receiverService.UpdateReceiverAsync(userId.Value, id, dto);
            return Ok(receiver);
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

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteReceiver(Guid id)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var result = await _receiverService.DeleteReceiverAsync(userId.Value, id);
            if (!result)
                return NotFound(new { error = "Грузополучатель не найден" });

            return NoContent();
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpPut("{id}/set-default")]
    public async Task<IActionResult> SetDefaultReceiver(Guid id)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var receiver = await _receiverService.SetDefaultReceiverAsync(userId.Value, id);
            return Ok(receiver);
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

