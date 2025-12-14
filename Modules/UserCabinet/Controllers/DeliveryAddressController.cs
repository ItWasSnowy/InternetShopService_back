using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Helpers;
using InternetShopService_back.Modules.UserCabinet.Repositories;
using InternetShopService_back.Modules.UserCabinet.Services;

namespace InternetShopService_back.Modules.UserCabinet.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DeliveryAddressController : ControllerBase
{
    private readonly IDeliveryAddressService _addressService;
    private readonly IUserAccountRepository _userAccountRepository;

    public DeliveryAddressController(
        IDeliveryAddressService addressService,
        IUserAccountRepository userAccountRepository)
    {
        _addressService = addressService;
        _userAccountRepository = userAccountRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetAddresses()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var addresses = await _addressService.GetAddressesAsync(userId.Value);
            return Ok(addresses);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetAddress(Guid id)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var address = await _addressService.GetAddressAsync(userId.Value, id);
            if (address == null)
                return NotFound(new { error = "Адрес не найден" });

            return Ok(address);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpGet("default")]
    public async Task<IActionResult> GetDefaultAddress()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var address = await _addressService.GetDefaultAddressAsync(userId.Value);
            if (address == null)
                return NotFound(new { error = "Адрес по умолчанию не найден" });

            return Ok(address);
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateAddress([FromBody] CreateDeliveryAddressDto dto)
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
            var address = await _addressService.CreateAddressAsync(userId.Value, dto);
            return CreatedAtAction(nameof(GetAddress), new { id = address.Id }, address);
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
    public async Task<IActionResult> UpdateAddress(Guid id, [FromBody] UpdateDeliveryAddressDto dto)
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
            var address = await _addressService.UpdateAddressAsync(userId.Value, id, dto);
            return Ok(address);
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
    public async Task<IActionResult> DeleteAddress(Guid id)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var result = await _addressService.DeleteAddressAsync(userId.Value, id);
            if (!result)
                return NotFound(new { error = "Адрес не найден" });

            return NoContent();
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    [HttpPut("{id}/set-default")]
    public async Task<IActionResult> SetDefaultAddress(Guid id)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var address = await _addressService.SetDefaultAddressAsync(userId.Value, id);
            return Ok(address);
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

    [HttpGet("last-delivery-type")]
    public async Task<IActionResult> GetLastDeliveryType()
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var userAccount = await _userAccountRepository.GetByIdAsync(userId.Value);
            if (userAccount == null)
            {
                return NotFound(new { error = "Пользователь не найден" });
            }

            return Ok(new
            {
                lastDeliveryType = userAccount.LastDeliveryType,
                lastDeliveryTypeName = userAccount.LastDeliveryType.HasValue
                    ? GetDeliveryTypeName(userAccount.LastDeliveryType.Value)
                    : null
            });
        }
        catch (Exception)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера" });
        }
    }

    private static string GetDeliveryTypeName(DeliveryType deliveryType)
    {
        return deliveryType switch
        {
            DeliveryType.Pickup => "Самовывоз",
            DeliveryType.Carrier => "Транспортная компания",
            DeliveryType.SellerDelivery => "Доставка средствами продавца",
            _ => "Неизвестный способ доставки"
        };
    }
}

