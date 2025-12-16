using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InternetShopService_back.Modules.OrderManagement.Controllers;

[ApiController]
[Route("api/files")]
[AllowAnonymous] // Разрешаем доступ без авторизации для статических файлов
public class FileController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<FileController> _logger;

    public FileController(IWebHostEnvironment environment, ILogger<FileController> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    [HttpGet("uploads/orders/{orderId}/comments/{fileName}")]
    public IActionResult GetCommentFile(string orderId, string fileName)
    {
        try
        {
            var filePath = Path.Combine(_environment.WebRootPath, "uploads", "orders", orderId, "comments", fileName);
            
            if (!System.IO.File.Exists(filePath))
            {
                _logger.LogWarning("Файл не найден: {FilePath}", filePath);
                return NotFound(new { error = "Файл не найден" });
            }

            var contentType = GetContentType(fileName);
            var fileBytes = System.IO.File.ReadAllBytes(filePath);
            
            return File(fileBytes, contentType, fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении файла {FileName} для заказа {OrderId}", fileName, orderId);
            return StatusCode(500, new { error = "Ошибка при получении файла" });
        }
    }

    private string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };
    }
}

