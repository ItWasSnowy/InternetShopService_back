namespace InternetShopService_back.Infrastructure.Notifications;

public interface IEmailService
{
    Task SendEmailAsync(string to, string subject, string body);
    Task SendOrderStatusNotificationAsync(string email, Guid orderId, string status);
}

