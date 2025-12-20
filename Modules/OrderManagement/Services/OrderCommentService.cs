using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GrpcOrderComment = InternetShopService_back.Infrastructure.Grpc.Orders.OrderComment;
using GrpcAttachedFile = InternetShopService_back.Infrastructure.Grpc.Orders.AttachedFile;
using LocalOrderComment = InternetShopService_back.Modules.OrderManagement.Models.OrderComment;
using LocalOrder = InternetShopService_back.Modules.OrderManagement.Models.Order;

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

        // ✅ КРИТИЧЕСКОЕ ИСПРАВЛЕНИЕ: Перезагружаем заказ из БД для получения актуальных данных
        // Это решает проблему кэширования Entity Framework, когда FimBizOrderId может быть обновлен
        // в другом контексте после отправки заказа в FimBiz, но текущий контекст содержит устаревшие данные
        _logger.LogDebug("Перезагружаем заказ {OrderId} для получения актуальных данных перед созданием комментария", dto.OrderId);
        
        // Простое решение: делаем новый запрос к БД для получения свежих данных
        var freshOrder = await _orderRepository.GetByIdAsync(dto.OrderId);
        if (freshOrder != null)
        {
            // Обновляем критические поля из свежих данных
            order.FimBizOrderId = freshOrder.FimBizOrderId;
            order.SyncedWithFimBizAt = freshOrder.SyncedWithFimBizAt;
            order.OrderNumber = freshOrder.OrderNumber;
        }

        _logger.LogInformation("Создание комментария для заказа {OrderId}. Актуальные данные: FimBizOrderId={FimBizOrderId}, SyncedWithFimBizAt={SyncedWithFimBizAt}, CreatedAt={CreatedAt}", 
            order.Id, order.FimBizOrderId, order.SyncedWithFimBizAt, order.CreatedAt);

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
                // 🔥 ИСПРАВЛЕНИЕ RACE CONDITION: Если заказ только что синхронизирован (< 5 секунд),
                // добавляем задержку перед отправкой первого комментария, чтобы FimBiz успел обработать заказ
                if (order.SyncedWithFimBizAt.HasValue)
                {
                    var timeSinceSync = (DateTime.UtcNow - order.SyncedWithFimBizAt.Value).TotalSeconds;
                    if (timeSinceSync < 5)
                    {
                        var delaySeconds = 3; // Задержка 3 секунды для первого комментария
                        _logger.LogInformation(
                            "Заказ {OrderId} синхронизирован недавно ({TimeSinceSync:F1} сек назад). " +
                            "Добавляем задержку {DelaySeconds} сек перед отправкой первого комментария, чтобы FimBiz успел обработать заказ.",
                            order.Id, timeSinceSync, delaySeconds);
                        
                        await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        
                        // Перезагружаем заказ для получения актуальных данных
                        var refreshedOrder = await _orderRepository.GetByIdAsync(dto.OrderId);
                        if (refreshedOrder != null && refreshedOrder.FimBizOrderId.HasValue)
                        {
                            order.FimBizOrderId = refreshedOrder.FimBizOrderId;
                            order.SyncedWithFimBizAt = refreshedOrder.SyncedWithFimBizAt;
                            order.OrderNumber = refreshedOrder.OrderNumber;
                        }
                    }
                }
                
                await SendCommentToFimBizAsync(order, comment, externalCommentId, dto.CommentText, dto.AuthorName);
            }
            else
            {
                _logger.LogWarning("Заказ {OrderId} не синхронизирован с FimBiz, комментарий не будет отправлен. FimBizOrderId={FimBizOrderId}, SyncedWithFimBizAt={SyncedWithFimBizAt}", 
                    order.Id, order.FimBizOrderId, order.SyncedWithFimBizAt);
                    
                // 🔥 ФИНАЛЬНАЯ ПОПЫТКА: Если заказ был недавно создан, ждем немного и проверяем еще раз
                if (order.SyncedWithFimBizAt == null && (DateTime.UtcNow - order.CreatedAt).TotalSeconds < 10)
                {
                    _logger.LogInformation("Заказ {OrderId} создан недавно ({CreatedSecondsAgo} сек назад). Делаем финальную попытку получить актуальные данные через 2 секунды...", 
                        order.Id, (DateTime.UtcNow - order.CreatedAt).TotalSeconds);
                        
                    await Task.Delay(2000); // Ждем 2 секунды
                    
                    // Последняя попытка получить актуальные данные
                    var finalOrder = await _orderRepository.GetByIdAsync(dto.OrderId);
                    if (finalOrder != null && finalOrder.FimBizOrderId.HasValue)
                    {
                        _logger.LogInformation("🎉 УСПЕХ! Заказ {OrderId} теперь синхронизирован. FimBizOrderId={FimBizOrderId}. Отправляем комментарий...", 
                            finalOrder.Id, finalOrder.FimBizOrderId);
                            
                        // Обновляем данные заказа
                        order.FimBizOrderId = finalOrder.FimBizOrderId;
                        order.SyncedWithFimBizAt = finalOrder.SyncedWithFimBizAt;
                        order.OrderNumber = finalOrder.OrderNumber;
                        
                        // Рекурсивно вызываем отправку комментария
                        await SendCommentToFimBizAsync(order, comment, externalCommentId, dto.CommentText, dto.AuthorName);
                    }
                    else
                    {
                        _logger.LogWarning("❌ Заказ {OrderId} все еще не синхронизирован после ожидания. Комментарий будет отправлен позже через SendUnsentCommentsToFimBizAsync", order.Id);
                    }
                }
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

        // Формируем полный URL файла
        var baseUrl = _configuration["AppSettings:BaseUrl"] 
            ?? _configuration["AppSettings:PublicUrl"]
            ?? throw new InvalidOperationException("AppSettings:BaseUrl или AppSettings:PublicUrl должен быть настроен для загрузки файлов");
        
        var fullUrl = GetPublicFileUrl(baseUrl, relativePath);

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
    /// Формирует полный публичный URL для файла
    /// </summary>
    private static string GetPublicFileUrl(string baseUrl, string relativePath)
    {
        baseUrl = baseUrl.TrimEnd('/');
        
        // Убеждаемся, что относительный путь начинается с /
        if (!relativePath.StartsWith('/'))
        {
            relativePath = "/" + relativePath;
        }
        
        return $"{baseUrl}{relativePath}";
    }

    /// <summary>
    /// Отправляет неотправленные комментарии заказа в FimBiz после синхронизации заказа
    /// </summary>
    public async Task SendUnsentCommentsToFimBizAsync(Guid orderId)
    {
        var order = await _orderRepository.GetByIdAsync(orderId);
        if (order == null || !order.FimBizOrderId.HasValue)
        {
            _logger.LogInformation("Заказ {OrderId} не найден или не синхронизирован с FimBiz, пропускаем отправку комментариев. Order found: {OrderFound}, FimBizOrderId: {FimBizOrderId}", 
                orderId, order != null, order?.FimBizOrderId);
            return;
        }
        
        _logger.LogInformation("🔄 Начинаем отправку неотправленных комментариев для заказа {OrderId}. FimBizOrderId: {FimBizOrderId}, SyncedWithFimBizAt: {SyncedWithFimBizAt}", 
            order.Id, order.FimBizOrderId, order.SyncedWithFimBizAt);

        // Получаем все комментарии заказа, созданные в интернет-магазине
        var comments = await _commentRepository.GetByOrderIdAsync(orderId);
        var unsentComments = comments
            .Where(c => c.IsFromInternetShop)
            .OrderBy(c => c.CreatedAt)
            .ToList();

        if (!unsentComments.Any())
        {
            _logger.LogDebug("Нет комментариев для отправки в FimBiz для заказа {OrderId}", orderId);
            return;
        }

        _logger.LogInformation("Найдено {Count} комментариев для отправки в FimBiz для заказа {OrderId}", 
            unsentComments.Count, orderId);

        // Определяем правильный ExternalOrderId
        // После синхронизации заказ в FimBiz всегда хранится с ExternalOrderId = "FIMBIZ-{FimBizOrderId}"
        // независимо от того, где был создан заказ изначально
        string externalOrderId = $"FIMBIZ-{order.FimBizOrderId.Value}";
        _logger.LogInformation("Используем ExternalOrderId для неотправленных комментариев: {ExternalOrderId} (FimBizOrderId: {FimBizOrderId}, OrderId: {OrderId})", 
            externalOrderId, order.FimBizOrderId.Value, order.Id);

        int sentCount = 0;
        int skippedCount = 0;

        foreach (var comment in unsentComments)
        {
            try
            {
                // Определяем, является ли это первым комментарием
                bool isFirstComment = order.SyncedWithFimBizAt.HasValue && 
                                      (comment.CreatedAt - order.SyncedWithFimBizAt.Value).TotalSeconds < 10;

                var grpcComment = new GrpcOrderComment
                {
                    CommentId = comment.ExternalCommentId,
                    ExternalOrderId = externalOrderId,
                    FimBizOrderId = order.FimBizOrderId.Value,
                    CommentText = comment.CommentText,
                    CreatedAt = ((DateTimeOffset)comment.CreatedAt).ToUnixTimeSeconds(),
                    AuthorName = comment.AuthorName ?? string.Empty,
                    IsFromInternetShop = true
                };

                // Добавляем прикрепленные файлы
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

                // Retry-логика с экспоненциальной задержкой для первого комментария
                int maxRetries = isFirstComment ? 3 : 1;
                int retryCount = 0;
                bool commentSent = false;

                while (retryCount < maxRetries && !commentSent)
                {
                    try
                    {
                        if (retryCount > 0)
                        {
                            var delaySeconds = (int)Math.Pow(2, retryCount); // 2s, 4s, 8s
                            _logger.LogInformation(
                                "🔄 Повторная попытка отправки комментария {CommentId} (попытка {RetryCount}/{MaxRetries}). Задержка: {DelaySeconds} сек.",
                                comment.ExternalCommentId, retryCount + 1, maxRetries, delaySeconds);
                            await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                        }

                        var response = await _fimBizGrpcClient.CreateCommentAsync(request);
                        
                        if (!response.Success)
                        {
                            // Проверяем, не является ли это дублированием
                            if (response.Message != null && 
                                (response.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                                 response.Message.Contains("уже существует", StringComparison.OrdinalIgnoreCase) ||
                                 response.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                                 response.Message.Contains("дубликат", StringComparison.OrdinalIgnoreCase)))
                            {
                                _logger.LogInformation("✅ Комментарий {CommentId} уже существует в FimBiz (дублирование). Пропускаем.", 
                                    comment.ExternalCommentId);
                                skippedCount++;
                                commentSent = true;
                            }
                            else if (isFirstComment && retryCount < maxRetries - 1)
                            {
                                retryCount++;
                                _logger.LogWarning(
                                    "⚠️ Не удалось отправить первый комментарий {CommentId} в FimBiz: {Message}. Будет повторная попытка ({RetryCount}/{MaxRetries}).",
                                    comment.ExternalCommentId, response.Message, retryCount + 1, maxRetries);
                                continue;
                            }
                            else
                            {
                                _logger.LogWarning("❌ Не удалось отправить комментарий {CommentId} в FimBiz: {Message}", 
                                    comment.ExternalCommentId, response.Message);
                                break;
                            }
                        }
                        else
                        {
                            sentCount++;
                            _logger.LogInformation("✅ Комментарий {CommentId} успешно отправлен в FimBiz. RetryCount: {RetryCount}", 
                                comment.ExternalCommentId, retryCount);
                            commentSent = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        retryCount++;
                        if (isFirstComment && retryCount < maxRetries)
                        {
                            _logger.LogWarning(ex, 
                                "⚠️ Ошибка при отправке первого комментария {CommentId} в FimBiz (попытка {RetryCount}/{MaxRetries}). Будет повторная попытка.",
                                comment.ExternalCommentId, retryCount, maxRetries);
                            continue;
                        }
                        else
                        {
                            _logger.LogError(ex, "❌ Ошибка при отправке комментария {CommentId} в FimBiz после {RetryCount} попыток", 
                                comment.ExternalCommentId, retryCount);
                            break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке комментария {CommentId} в FimBiz", comment.ExternalCommentId);
            }
        }

        _logger.LogInformation(
            "Отправка комментариев для заказа {OrderId} завершена. Отправлено: {SentCount}, Пропущено (дубликаты): {SkippedCount}, Всего: {TotalCount}",
            orderId, sentCount, skippedCount, unsentComments.Count);
    }

    /// <summary>
    /// Вспомогательный метод для отправки комментария в FimBiz
    /// </summary>
    private async Task SendCommentToFimBizAsync(LocalOrder order, LocalOrderComment comment, string externalCommentId, string commentText, string? authorName)
    {
        if (!order.FimBizOrderId.HasValue)
        {
            _logger.LogWarning("Невозможно отправить комментарий {CommentId}: заказ {OrderId} не имеет FimBizOrderId", externalCommentId, order.Id);
            return;
        }

        // Определяем правильный ExternalOrderId
        // После синхронизации заказ в FimBiz всегда хранится с ExternalOrderId = "FIMBIZ-{FimBizOrderId}"
        // независимо от того, где был создан заказ изначально
        string externalOrderId = $"FIMBIZ-{order.FimBizOrderId.Value}";
        _logger.LogInformation("Используем ExternalOrderId для комментария: {ExternalOrderId} (FimBizOrderId: {FimBizOrderId}, OrderId: {OrderId})", 
            externalOrderId, order.FimBizOrderId.Value, order.Id);

        var grpcComment = new GrpcOrderComment
        {
            CommentId = externalCommentId,
            ExternalOrderId = externalOrderId,
            FimBizOrderId = order.FimBizOrderId.Value,
            CommentText = commentText,
            CreatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            AuthorName = authorName ?? string.Empty,
            IsFromInternetShop = true
        };

        // Добавляем прикрепленные файлы
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

        // Определяем, является ли это первым комментарием (созданным сразу после синхронизации)
        bool isFirstComment = order.SyncedWithFimBizAt.HasValue && 
                              (DateTime.UtcNow - order.SyncedWithFimBizAt.Value).TotalSeconds < 10;

        // Детальное логирование запроса
        _logger.LogInformation(
            "📤 Отправка комментария в FimBiz. CommentId: {CommentId}, ExternalOrderId: {ExternalOrderId}, FimBizOrderId: {FimBizOrderId}, CommentText: {CommentText}, AuthorName: {AuthorName}, AttachmentsCount: {AttachmentsCount}, IsFirstComment: {IsFirstComment}",
            externalCommentId, externalOrderId, order.FimBizOrderId.Value, 
            commentText?.Substring(0, Math.Min(100, commentText?.Length ?? 0)) ?? "", 
            authorName ?? "", 
            comment.Attachments?.Count ?? 0,
            isFirstComment);

        // Retry-логика с экспоненциальной задержкой для первого комментария
        int maxRetries = isFirstComment ? 3 : 1;
        int retryCount = 0;
        bool success = false;

        while (retryCount < maxRetries && !success)
        {
            try
            {
                if (retryCount > 0)
                {
                    var delaySeconds = (int)Math.Pow(2, retryCount); // 2s, 4s, 8s
                    _logger.LogInformation(
                        "🔄 Повторная попытка отправки комментария {CommentId} (попытка {RetryCount}/{MaxRetries}). Задержка: {DelaySeconds} сек.",
                        externalCommentId, retryCount + 1, maxRetries, delaySeconds);
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                }

                var response = await _fimBizGrpcClient.CreateCommentAsync(request);
                
                // Детальное логирование ответа
                _logger.LogInformation(
                    "📥 Ответ от FimBiz для комментария {CommentId}. Success: {Success}, Message: {Message}, RetryCount: {RetryCount}",
                    externalCommentId, response.Success, response.Message ?? "нет сообщения", retryCount);
                
                if (!response.Success)
                {
                    // Обработка дублирования комментария
                    if (response.Message != null && 
                        (response.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
                         response.Message.Contains("уже существует", StringComparison.OrdinalIgnoreCase) ||
                         response.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
                         response.Message.Contains("дубликат", StringComparison.OrdinalIgnoreCase)))
                    {
                        _logger.LogInformation(
                            "✅ Комментарий {CommentId} уже существует в FimBiz (дублирование). ExternalOrderId: {ExternalOrderId}, Message: {Message}",
                            externalCommentId, externalOrderId, response.Message);
                        success = true; // Дубликат считается успехом
                        break;
                    }
                    else if (isFirstComment && retryCount < maxRetries - 1)
                    {
                        // Для первого комментария делаем retry при любой ошибке
                        retryCount++;
                        _logger.LogWarning(
                            "⚠️ Не удалось отправить первый комментарий {CommentId} в FimBiz: {Message}. Будет повторная попытка ({RetryCount}/{MaxRetries}).",
                            externalCommentId, response.Message, retryCount + 1, maxRetries);
                        continue;
                    }
                    else
                    {
                        _logger.LogWarning(
                            "❌ Не удалось отправить комментарий {CommentId} в FimBiz. ExternalOrderId: {ExternalOrderId}, Message: {Message}", 
                            externalCommentId, externalOrderId, response.Message);
                        success = false;
                        break;
                    }
                }
                else
                {
                    _logger.LogInformation(
                        "✅ Комментарий {CommentId} успешно отправлен в FimBiz. ExternalOrderId: {ExternalOrderId}, RetryCount: {RetryCount}", 
                        externalCommentId, externalOrderId, retryCount);
                    success = true;
                    break;
                }
            }
            catch (Exception ex)
            {
                retryCount++;
                if (isFirstComment && retryCount < maxRetries)
                {
                    _logger.LogWarning(ex, 
                        "⚠️ Ошибка при отправке первого комментария {CommentId} в FimBiz (попытка {RetryCount}/{MaxRetries}). Будет повторная попытка.",
                        externalCommentId, retryCount, maxRetries);
                    continue;
                }
                else
                {
                    _logger.LogError(ex, "❌ Ошибка при отправке комментария {CommentId} в FimBiz после {RetryCount} попыток", 
                        externalCommentId, retryCount);
                    break;
                }
            }
        }

        if (!success && isFirstComment)
        {
            _logger.LogWarning(
                "⚠️ Не удалось отправить первый комментарий {CommentId} в FimBiz после {MaxRetries} попыток. " +
                "Комментарий будет отправлен позже через SendUnsentCommentsToFimBizAsync.",
                externalCommentId, maxRetries);
        }
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

