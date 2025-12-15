using InternetShopService_back.Modules.OrderManagement.DTOs;
using InternetShopService_back.Modules.OrderManagement.Models;
using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.OrderManagement.Services;

public interface IOrderService
{
    Task<OrderDto> CreateOrderAsync(CreateOrderDto dto);
    Task<OrderDto> CreateOrderFromCartAsync(Guid userId, CreateOrderFromCartDto dto, List<CreateOrderItemDto> items);
    Task<OrderDto> GetOrderAsync(Guid orderId);
    Task<List<OrderDto>> GetOrdersByUserAsync(Guid userId);
    Task<PagedResult<OrderDto>> GetOrdersByUserPagedAsync(Guid userId, int page, int pageSize);
    Task<OrderDto> UpdateOrderStatusAsync(Guid orderId, OrderStatus status);
    
    /// <summary>
    /// Запрашивает звонок для подтверждения счета (для постоплаты)
    /// </summary>
    Task RequestInvoiceConfirmationCodeAsync(Guid orderId, Guid userId);
    
    /// <summary>
    /// Подтверждает счет по коду из звонка (для постоплаты)
    /// </summary>
    Task<OrderDto> ConfirmInvoiceByPhoneAsync(Guid orderId, Guid userId, string code);
    
    /// <summary>
    /// Загружает файл к заказу
    /// </summary>
    Task<OrderAttachmentDto> UploadAttachmentAsync(Guid orderId, Guid userId, Microsoft.AspNetCore.Http.IFormFile file);
}

