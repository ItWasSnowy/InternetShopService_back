using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Services;
using InternetShopService_back.Modules.UserCabinet.Helpers;

namespace InternetShopService_back.Modules.OrderManagement.Controllers;

[ApiController]
[Route("api/orders/{orderId}/comments")]
[Authorize]
public class OrderCommentsController : ControllerBase
{
    private readonly IOrderCommentService _commentService;

    public OrderCommentsController(IOrderCommentService commentService)
    {
        _commentService = commentService;
    }

    /// <summary>
    /// Получить все комментарии к заказу
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetComments(Guid orderId)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var comments = await _commentService.GetCommentsByOrderIdAsync(orderId);
            return Ok(comments);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера", message = ex.Message });
        }
    }

    /// <summary>
    /// Создать комментарий к заказу
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateComment(Guid orderId, [FromBody] CreateOrderCommentDto dto)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            // Устанавливаем OrderId из маршрута
            dto.OrderId = orderId;

            var comment = await _commentService.CreateCommentAsync(dto, userId.Value);
            return CreatedAtAction(nameof(GetComment), new { orderId, commentId = comment.Id }, comment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера", message = ex.Message });
        }
    }

    /// <summary>
    /// Загрузить файл для комментария к заказу
    /// </summary>
    [HttpPost("attachments")]
    [RequestSizeLimit(50 * 1024 * 1024)] // Максимальный размер файла: 50 МБ
    public async Task<IActionResult> UploadAttachment(Guid orderId, IFormFile file)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "Файл не указан или пуст" });
        }

        try
        {
            var attachment = await _commentService.UploadAttachmentAsync(orderId, userId.Value, file);
            return Ok(attachment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера", message = ex.Message });
        }
    }

    /// <summary>
    /// Получить комментарий по ID
    /// </summary>
    [HttpGet("{commentId}")]
    public async Task<IActionResult> GetComment(Guid orderId, Guid commentId)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var comment = await _commentService.GetCommentByIdAsync(commentId);
            if (comment == null || comment.OrderId != orderId)
            {
                return NotFound(new { error = "Комментарий не найден" });
            }

            return Ok(comment);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера", message = ex.Message });
        }
    }

    /// <summary>
    /// Обновить комментарий (только автор может обновлять)
    /// </summary>
    [HttpPut("{commentId}")]
    public async Task<IActionResult> UpdateComment(Guid orderId, Guid commentId, [FromBody] UpdateOrderCommentDto dto)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var comment = await _commentService.GetCommentByIdAsync(commentId);
            if (comment == null || comment.OrderId != orderId)
            {
                return NotFound(new { error = "Комментарий не найден" });
            }

            var updatedComment = await _commentService.UpdateCommentAsync(commentId, dto, userId.Value);
            return Ok(updatedComment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (UnauthorizedAccessException ex)
        {
            return Unauthorized(new { error = ex.Message });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера", message = ex.Message });
        }
    }

    /// <summary>
    /// Удалить комментарий
    /// </summary>
    [HttpDelete("{commentId}")]
    public async Task<IActionResult> DeleteComment(Guid orderId, Guid commentId)
    {
        var userId = HttpContext.GetUserId();
        if (userId == null)
        {
            return Unauthorized(new { error = "Пользователь не авторизован" });
        }

        try
        {
            var comment = await _commentService.GetCommentByIdAsync(commentId);
            if (comment == null || comment.OrderId != orderId)
            {
                return NotFound(new { error = "Комментарий не найден" });
            }

            var deleted = await _commentService.DeleteCommentAsync(commentId);
            if (!deleted)
            {
                return NotFound(new { error = "Комментарий не найден" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Внутренняя ошибка сервера", message = ex.Message });
        }
    }
}

