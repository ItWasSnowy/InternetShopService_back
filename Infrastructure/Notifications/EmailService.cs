namespace InternetShopService_back.Infrastructure.Notifications;

public class EmailService : IEmailService
{
    public Task SendEmailAsync(string to, string subject, string body)
    {
        // TODO: Implement email sending
        Console.WriteLine($"Email to {to}: {subject} - {body}");
        return Task.CompletedTask;
    }

    public Task SendOrderStatusNotificationAsync(string email, Guid orderId, string status)
    {
        var subject = $"Статус заказа #{orderId} изменен";
        var body = $"Ваш заказ #{orderId} перешел в статус: {status}";
        return SendEmailAsync(email, subject, body);
    }
}

