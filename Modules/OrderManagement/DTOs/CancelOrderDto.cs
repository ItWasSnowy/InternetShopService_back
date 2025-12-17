namespace InternetShopService_back.Modules.OrderManagement.DTOs;

/// <summary>
/// DTO для отмены заказа
/// </summary>
public class CancelOrderDto
{
    /// <summary>
    /// Причина отмены (опционально)
    /// </summary>
    public string? Reason { get; set; }
}
