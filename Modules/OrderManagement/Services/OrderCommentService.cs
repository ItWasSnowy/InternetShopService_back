using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GrpcOrderComment = InternetShopService_back.Infrastructure.Grpc.Orders.OrderComment;
using GrpcAttachedFile = InternetShopService_back.Infrastructure.Grpc.Orders.AttachedFile;
using LocalOrderComment = InternetShopService_back.Modules.OrderManagement.Models.OrderComment;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public class OrderCommentService : IOrderCommentService
{
    private readonly IOrderCommentRepository _commentRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IFimBizGrpcClient _fimBizGrpcClient;
    private readonly ILogger<OrderCommentService> _logger;
    private readonly IConfiguration _configuration;

    public OrderCommentService(
        IOrderCommentRepository commentRepository,
        IOrderRepository orderRepository,
        IFimBizGrpcClient fimBizGrpcClient,
        ILogger<OrderCommentService> logger,
        IConfiguration configuration)
    {
        _commentRepository = commentRepository;
        _orderRepository = orderRepository;
        _fimBizGrpcClient = fimBizGrpcClient;
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<OrderCommentDto> CreateCommentAsync(CreateOrderCommentDto dto, Guid userId)
    {
        // Проверяем существование заказа
        var order = await _orderRepository.GetByIdAsync(dto.OrderId);
        if (order == null)
        {
            throw new InvalidOperationException("Заказ не найден");
        }

        // Проверяем, что заказ принадлежит пользователю
        if (order.UserAccountId != userId)
        {
            throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");
        }

        // Генерируем уникальный ID для комментария
        var externalCommentId = Guid.NewGuid().ToString();

        // Создаем комментарий
        var comment = new LocalOrderComment
        {
            Id = Guid.NewGuid(),
            OrderId = dto.OrderId,
            ExternalCommentId = externalCommentId,
            CommentText = dto.CommentText,
            AuthorUserId = userId,
            AuthorName = dto.AuthorName,
            IsFromInternetShop = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Добавляем прикрепленные файлы
        foreach (var attachmentDto in dto.Attachments)
        {
            var attachment = new OrderCommentAttachment
            {
                Id = Guid.NewGuid(),
                OrderCommentId = comment.Id,
                FileName = attachmentDto.FileName,
                ContentType = attachmentDto.ContentType,
                FileUrl = attachmentDto.FileUrl,
                CreatedAt = DateTime.UtcNow
            };
            comment.Attachments.Add(attachment);
        }

        // Сохраняем комментарий в локальной БД
        await _commentRepository.CreateAsync(comment);

        // Отправляем комментарий в FimBiz через gRPC
        try
        {
            if (order.FimBizOrderId.HasValue)
            {
                var grpcComment = new GrpcOrderComment
                {
                    CommentId = externalCommentId,
                    ExternalOrderId = order.Id.ToString(),
                    FimBizOrderId = order.FimBizOrderId.Value,
                    CommentText = dto.CommentText,
                    CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    AuthorName = dto.AuthorName ?? string.Empty,
                    IsFromInternetShop = true
                };

                // Добавляем прикрепленные файлы в gRPC сообщение
                foreach (var attachment in comment.Attachments)
                {
                    grpcComment.AttachedFiles.Add(new GrpcAttachedFile
                    {
                        FileName = attachment.FileName,
                        ContentType = attachment.ContentType,
                        Url = attachment.FileUrl
                    });
                }

                var request = new CreateCommentRequest
                {
                    Comment = grpcComment
                };

                var response = await _fimBizGrpcClient.CreateCommentAsync(request);
                if (!response.Success)
                {
                    _logger.LogWarning("Не удалось отправить комментарий в FimBiz: {Message}", response.Message);
                }
                else
                {
                    _logger.LogInformation("Комментарий {CommentId} успешно отправлен в FimBiz", externalCommentId);
                }
            }
            else
            {
                _logger.LogWarning("Заказ {OrderId} не синхронизирован с FimBiz, комментарий не будет отправлен", order.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке комментария {CommentId} в FimBiz", externalCommentId);
            // Не прерываем выполнение, комментарий уже сохранен локально
        }

        return MapToDto(comment);
    }

    public async Task<List<OrderCommentDto>> GetCommentsByOrderIdAsync(Guid orderId)
    {
        var comments = await _commentRepository.GetByOrderIdAsync(orderId);
        return comments.Select(MapToDto).ToList();
    }

    public async Task<OrderCommentDto?> GetCommentByIdAsync(Guid commentId)
    {
        var comment = await _commentRepository.GetByIdAsync(commentId);
        return comment != null ? MapToDto(comment) : null;
    }

    public async Task<OrderCommentDto> UpdateCommentAsync(Guid commentId, UpdateOrderCommentDto dto, Guid userId)
    {
        // Получаем комментарий
        var comment = await _commentRepository.GetByIdAsync(commentId);
        if (comment == null)
        {
            throw new InvalidOperationException("Комментарий не найден");
        }

        // Проверяем, что комментарий был создан в интернет-магазине
        if (!comment.IsFromInternetShop)
        {
            throw new UnauthorizedAccessException("Комментарии из FimBiz нельзя редактировать");
        }

        // Проверяем, что текущий пользователь является автором комментария
        if (comment.AuthorUserId != userId)
        {
            throw new UnauthorizedAccessException("Только автор комментария может его редактировать");
        }

        // Обновляем текст комментария
        comment.CommentText = dto.CommentText;
        comment.UpdatedAt = DateTime.UtcNow;

        // Сохраняем изменения
        var updatedComment = await _commentRepository.UpdateAsync(comment);

        _logger.LogInformation("Комментарий {CommentId} обновлен пользователем {UserId}", commentId, userId);

        return MapToDto(updatedComment);
    }

    public async Task<bool> DeleteCommentAsync(Guid commentId)
    {
        return await _commentRepository.DeleteAsync(commentId);
    }

    public async Task<OrderCommentAttachmentDto> UploadAttachmentAsync(Guid orderId, Guid userId, IFormFile file)
    {
        // Проверяем, что файл передан
        if (file == null || file.Length == 0)
        {
            throw new InvalidOperationException("Файл не указан или пуст");
        }

        // Проверяем размер файла (максимум 50 МБ)
        const long maxFileSize = 50 * 1024 * 1024; // 50 МБ
        if (file.Length > maxFileSize)
        {
            throw new InvalidOperationException($"Размер файла превышает максимально допустимый ({maxFileSize / 1024 / 1024} МБ)");
        }

        // Получаем заказ
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null)
        {
            throw new InvalidOperationException("Заказ не найден");
        }

        // Проверяем, что заказ принадлежит пользователю
        if (order.UserAccountId != userId)
        {
            throw new UnauthorizedAccessException("Заказ не принадлежит текущему пользователю");
        }

        // Сохраняем файл локально
        var relativePath = await SaveFileLocallyAsync(orderId, file.FileName, file);
        if (string.IsNullOrEmpty(relativePath))
        {
            throw new InvalidOperationException("Не удалось сохранить файл");
        }

        // Формируем полный URL файла через API контроллер (с SSL)
        var baseUrl = _configuration["AppSettings:BaseUrl"] 
            ?? _configuration["AppSettings:PublicUrl"]
            ?? throw new InvalidOperationException("AppSettings:BaseUrl или AppSettings:PublicUrl должен быть настроен для загрузки файлов");
        
        // Формируем URL через API контроллер - так будет работать SSL
        var fullUrl = GetPublicFileUrl(baseUrl, relativePath, orderId);

        _logger.LogInformation("Файл {FileName} успешно загружен для комментария к заказу {OrderId} пользователем {UserId}", 
            file.FileName, orderId, userId);

        return new OrderCommentAttachmentDto
        {
            Id = Guid.NewGuid(), // Временный ID, реальный будет присвоен при создании комментария
            FileName = file.FileName,
            ContentType = file.ContentType,
            FileUrl = fullUrl,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Сохранение файла локально (из IFormFile)
    /// </summary>
    private async Task<string?> SaveFileLocallyAsync(Guid orderId, string fileName, IFormFile file)
    {
        try
        {
            // Получаем путь для сохранения файлов из конфигурации
            var uploadsPath = _configuration["AppSettings:UploadsPath"] 
                ?? _configuration["AppSettings:FilesPath"]
                ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "orders");

            // Создаем директорию для комментариев к заказу
            var commentsDirectory = Path.Combine(uploadsPath, orderId.ToString(), "comments");
            Directory.CreateDirectory(commentsDirectory);

            // Генерируем уникальное имя файла (добавляем timestamp для избежания конфликтов)
            var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var safeFileName = Path.GetFileNameWithoutExtension(fileName);
            var extension = Path.GetExtension(fileName);
            var uniqueFileName = $"{safeFileName}_{timestamp}{extension}";

            var filePath = Path.Combine(commentsDirectory, uniqueFileName);

            // Сохраняем файл
            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            // Возвращаем относительный путь для формирования URL
            var relativePath = Path.Combine("uploads", "orders", orderId.ToString(), "comments", uniqueFileName)
                .Replace('\\', '/');

            return relativePath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении файла {FileName} локально", fileName);
            return null;
        }
    }

    /// <summary>
    /// Формирует полный публичный URL для файла через API контроллер
    /// </summary>
    private static string GetPublicFileUrl(string baseUrl, string relativePath, Guid orderId)
    {
        baseUrl = baseUrl.TrimEnd('/');
        
        // Извлекаем имя файла из относительного пути
        var fileName = Path.GetFileName(relativePath);
        
        // Формируем URL через API контроллер - так будет работать SSL
        return $"{baseUrl}/api/files/uploads/orders/{orderId}/comments/{fileName}";
    }

    private static OrderCommentDto MapToDto(LocalOrderComment comment)
    {
        return new OrderCommentDto
        {
            Id = comment.Id,
            OrderId = comment.OrderId,
            ExternalCommentId = comment.ExternalCommentId,
            CommentText = comment.CommentText,
            AuthorProfileId = comment.AuthorProfileId,
            AuthorUserId = comment.AuthorUserId,
            AuthorName = comment.AuthorName,
            IsFromInternetShop = comment.IsFromInternetShop,
            CreatedAt = comment.CreatedAt,
            Attachments = comment.Attachments.Select(a => new OrderCommentAttachmentDto
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                FileUrl = a.FileUrl,
                CreatedAt = a.CreatedAt
            }).ToList()
        };
    }
}

