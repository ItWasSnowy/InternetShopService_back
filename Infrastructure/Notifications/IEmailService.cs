namespace InternetShopService_back.Infrastructure.Notifications;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
    Task SendOrderStatusNotificationAsync(string email, Guid orderId, string status);
    Task SendBillNotificationAsync(string email, Guid orderId, string orderNumber, string? pdfUrl);
    Task SendOrderCancellationNotificationAsync(string email, Guid orderId, string orderNumber, string? reason);
}

