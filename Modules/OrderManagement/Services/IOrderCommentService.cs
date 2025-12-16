using InternetShopService_back.Modules.OrderManagement.DTOs;
using Microsoft.AspNetCore.Http;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public interface IOrderCommentService
{
    Task<OrderCommentDto> CreateCommentAsync(CreateOrderCommentDto dto, Guid userId);
    Task<OrderCommentDto> UpdateCommentAsync(Guid commentId, UpdateOrderCommentDto dto, Guid userId);
    Task<List<OrderCommentDto>> GetCommentsByOrderIdAsync(Guid orderId);
    Task<OrderCommentDto?> GetCommentByIdAsync(Guid commentId);
    Task<bool> DeleteCommentAsync(Guid commentId);
    Task<OrderCommentAttachmentDto> UploadAttachmentAsync(Guid orderId, Guid userId, IFormFile file);
}

