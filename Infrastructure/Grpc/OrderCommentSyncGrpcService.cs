using System.Linq;
using Grpc.Core;
using InternetShopService_back.Data;
using InternetShopService_back.Infrastructure.Grpc.Orders;
using InternetShopService_back.Infrastructure.SignalR;
using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.OrderManagement.Repositories;
using InternetShopService_back.Modules.Notifications.Models;
using InternetShopService_back.Modules.Notifications.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using GrpcOrderComment = InternetShopService_back.Infrastructure.Grpc.Orders.OrderComment;
using GrpcAttachedFile = InternetShopService_back.Infrastructure.Grpc.Orders.AttachedFile;
using LocalOrder = InternetShopService_back.Modules.OrderManagement.Models.Order;
using LocalOrderComment = InternetShopService_back.Modules.OrderManagement.Models.OrderComment;
using LocalOrderCommentAttachment = InternetShopService_back.Modules.OrderManagement.Models.OrderCommentAttachment;

namespace InternetShopService_back.Infrastructure.Grpc;

/// <summary>
/// gRPC сервис для обработки комментариев к заказам от FimBiz
/// </summary>
public class OrderCommentSyncGrpcService : OrderCommentSyncService.OrderCommentSyncServiceBase
{
    private readonly IOrderRepository _orderRepository;
    private readonly IOrderCommentRepository _commentRepository;
    private readonly IShopNotificationService _shopNotificationService;
    private readonly IShopNotificationsService _notificationsService;
    private readonly ILogger<OrderCommentSyncGrpcService> _logger;
    private readonly IConfiguration _configuration;
    private readonly ApplicationDbContext _dbContext;

    public OrderCommentSyncGrpcService(
        IOrderRepository orderRepository,
        IOrderCommentRepository commentRepository,
        IShopNotificationService shopNotificationService,
        IShopNotificationsService notificationsService,
        ILogger<OrderCommentSyncGrpcService> logger,
        IConfiguration configuration,
        ApplicationDbContext dbContext)
    {
        _orderRepository = orderRepository;
        _commentRepository = commentRepository;
        _shopNotificationService = shopNotificationService;
        _notificationsService = notificationsService;
        _logger = logger;
        _configuration = configuration;
        _dbContext = dbContext;
    }

    /// <summary>
    /// Обработка уведомления о создании комментария из FimBiz
    /// </summary>
    public override async Task<NotifyCommentCreatedResponse> NotifyCommentCreated(
        NotifyCommentCreatedRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation("=== [ORDER COMMENT] ВХОДЯЩИЙ ЗАПРОС NotifyCommentCreated ===");
        _logger.LogInformation("RemoteAddress: {RemoteAddress}", context.Peer);
        
        if (request.Comment != null)
        {
            _logger.LogInformation("Comment.CommentId: {CommentId}", request.Comment.CommentId);
            _logger.LogInformation("Comment.ExternalOrderId: {ExternalOrderId}", request.Comment.ExternalOrderId);
            _logger.LogInformation("Comment.FimBizOrderId: {FimBizOrderId}", request.Comment.FimBizOrderId);
        }

        try
        {
            // Проверка API ключа
            var apiKey = context.RequestHeaders.GetValue("x-api-key");
            var expectedApiKey = _configuration["FimBiz:ApiKey"];
            
            if (string.IsNullOrEmpty(apiKey) || apiKey != expectedApiKey)
            {
                _logger.LogWarning("Неверный или отсутствующий API ключ при создании комментария");
                throw new RpcException(new Status(StatusCode.Unauthenticated, "Invalid API key"));
            }

            if (request.Comment == null)
            {
                _logger.LogWarning("Получен запрос NotifyCommentCreated без Comment");
                throw new RpcException(new Status(StatusCode.InvalidArgument, "Comment is required"));
            }

            var grpcComment = request.Comment;

            // Находим заказ
            LocalOrder? order = null;
            Guid orderId = Guid.Empty;
            
            if (Guid.TryParse(grpcComment.ExternalOrderId, out var parsedGuid))
            {
                // Стандартный формат - Guid
                orderId = parsedGuid;
                order = await _orderRepository.GetByIdAsync(orderId);
            }
            else if (grpcComment.ExternalOrderId.StartsWith("FIMBIZ-", StringComparison.OrdinalIgnoreCase))
            {
                // Формат FIMBIZ-{orderId} - ищем по FimBizOrderId
                order = await _orderRepository.GetByFimBizOrderIdAsync(grpcComment.FimBizOrderId);
                if (order != null)
                {
                    orderId = order.Id;
                }
            }

            if (order == null)
            {
                _logger.LogWarning("Заказ с ExternalOrderId {ExternalOrderId} не найден", grpcComment.ExternalOrderId);
                throw new RpcException(
                    new Status(StatusCode.NotFound, 
                        $"Order with external ID {grpcComment.ExternalOrderId} not found"));
            }

            // Проверяем, не существует ли уже комментарий с таким ExternalCommentId
            var existingComment = await _commentRepository.GetByExternalCommentIdAsync(grpcComment.CommentId);
            if (existingComment != null)
            {
                _logger.LogWarning("Комментарий с ExternalCommentId {CommentId} уже существует", grpcComment.CommentId);
                return new NotifyCommentCreatedResponse
                {
                    Success = true,
                    Message = "Comment already exists"
                };
            }

            // Создаем комментарий в локальной БД
            var comment = new LocalOrderComment
            {
                Id = Guid.NewGuid(),
                OrderId = orderId,
                ExternalCommentId = grpcComment.CommentId,
                CommentText = grpcComment.CommentText,
                AuthorProfileId = grpcComment.HasAuthorProfileId && grpcComment.AuthorProfileId > 0 
                    ? grpcComment.AuthorProfileId 
                    : null,
                AuthorName = null, // Комментарий из FimBiz, имя берется из профиля
                IsFromInternetShop = false,
                CreatedAt = grpcComment.CreatedAt > 0 
                    ? DateTimeOffset.FromUnixTimeSeconds(grpcComment.CreatedAt).UtcDateTime 
                    : DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Добавляем прикрепленные файлы
            if (grpcComment.AttachedFiles != null && grpcComment.AttachedFiles.Count > 0)
            {
                foreach (var grpcFile in grpcComment.AttachedFiles)
                {
                    var attachment = new LocalOrderCommentAttachment
                    {
                        Id = Guid.NewGuid(),
                        OrderCommentId = comment.Id,
                        FileName = grpcFile.FileName,
                        ContentType = grpcFile.ContentType,
                        FileUrl = grpcFile.Url,
                        CreatedAt = DateTime.UtcNow
                    };
                    comment.Attachments.Add(attachment);
                }
            }

            await _commentRepository.CreateAsync(comment);

            _logger.LogInformation("Комментарий {CommentId} из FimBiz успешно сохранен для заказа {OrderId}",
                grpcComment.CommentId, orderId);

            await _notificationsService.CreateAsync(
                order.CounterpartyId,
                null,
                "Новый комментарий к заказу",
                null,
                ShopNotificationObjectType.Order,
                orderId);

            var dto = MapToDto(comment);
            await _shopNotificationService.OrderCommentAdded(order.CounterpartyId, dto);

            return new NotifyCommentCreatedResponse
            {
                Success = true,
                Message = "Comment created successfully"
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при обработке комментария из FimBiz");
            throw new RpcException(
                new Status(StatusCode.Internal, $"Internal error: {ex.Message}"));
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

