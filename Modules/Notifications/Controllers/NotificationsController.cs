using System;
using System.Threading.Tasks;
using InternetShopService_back.Modules.Notifications.DTOs;
using InternetShopService_back.Modules.Notifications.Services;
using InternetShopService_back.Modules.UserCabinet.Helpers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InternetShopService_back.Modules.Notifications.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize]
public class NotificationsController : ControllerBase
{
    private readonly IShopNotificationsService _notificationsService;

    public NotificationsController(IShopNotificationsService notificationsService)
    {
        _notificationsService = notificationsService;
    }

    [HttpGet]
    public async Task<ActionResult<PagedNotificationsDto>> GetList([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
    {
        var userId = HttpContext.GetUserId();
        var counterpartyId = HttpContext.GetCounterpartyId();

        if (userId == null || counterpartyId == null)
            return Unauthorized();

        var result = await _notificationsService.GetNotificationsAsync(userId.Value, counterpartyId.Value, page, pageSize);
        return Ok(result);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadNotificationsCountDto>> GetUnreadCount()
    {
        var userId = HttpContext.GetUserId();
        var counterpartyId = HttpContext.GetCounterpartyId();

        if (userId == null || counterpartyId == null)
            return Unauthorized();

        var count = await _notificationsService.GetUnreadCountAsync(userId.Value, counterpartyId.Value);
        return Ok(new UnreadNotificationsCountDto { Count = count });
    }

    [HttpPost("{id:guid}/read")]
    public async Task<ActionResult<ShopNotificationDto>> MarkAsRead([FromRoute] Guid id)
    {
        var userId = HttpContext.GetUserId();
        var counterpartyId = HttpContext.GetCounterpartyId();

        if (userId == null || counterpartyId == null)
            return Unauthorized();

        var updated = await _notificationsService.MarkAsReadAsync(id, userId.Value, counterpartyId.Value);
        if (updated == null)
            return NotFound();

        return Ok(updated);
    }

    [HttpPost("read-all")]
    public async Task<ActionResult> MarkAllAsRead()
    {
        var userId = HttpContext.GetUserId();
        var counterpartyId = HttpContext.GetCounterpartyId();

        if (userId == null || counterpartyId == null)
            return Unauthorized();

        var updated = await _notificationsService.MarkAllAsReadAsync(userId.Value, counterpartyId.Value);
        return Ok(new { updated });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete([FromRoute] Guid id)
    {
        var userId = HttpContext.GetUserId();
        var counterpartyId = HttpContext.GetCounterpartyId();

        if (userId == null || counterpartyId == null)
            return Unauthorized();

        var ok = await _notificationsService.DeleteAsync(id, userId.Value, counterpartyId.Value);
        if (!ok)
            return NotFound();

        return NoContent();
    }
}
