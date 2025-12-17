using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Notifications;

public class EmailService : IEmailService
{
    private readonly ILogger<EmailService> _logger;

    public EmailService(ILogger<EmailService> logger)
    {
        _logger = logger;
    }

    public Task SendEmailAsync(string to, string subject, string body)
    {
        // TODO: Implement email sending через SMTP или внешний сервис (SendGrid, Mailgun и т.д.)
        _logger.LogInformation("Email отправлен: To={To}, Subject={Subject}", to, subject);
        _logger.LogDebug("Email body: {Body}", body);
        
        // Временная заглушка - в продакшене здесь должна быть реальная отправка email
        Console.WriteLine($"Email to {to}: {subject} - {body}");
        return Task.CompletedTask;
    }

    public Task SendOrderStatusNotificationAsync(string email, Guid orderId, string status)
    {
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("Не указан email для отправки уведомления о заказе {OrderId}", orderId);
            return Task.CompletedTask;
        }

        var subject = $"Статус заказа изменен";
        var body = $"Ваш заказ #{orderId} перешел в статус: {status}";
        
        return SendEmailAsync(email, subject, body);
    }

    public Task SendBillNotificationAsync(string email, Guid orderId, string orderNumber, string? pdfUrl)
    {
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("Не указан email для отправки уведомления о счете для заказа {OrderId}", orderId);
            return Task.CompletedTask;
        }

        var subject = $"Создан счет на оплату для заказа #{orderNumber}";
        var pdfLink = !string.IsNullOrEmpty(pdfUrl) 
            ? $"<br><br>Скачать счет: <a href=\"{pdfUrl}\">{pdfUrl}</a>" 
            : "";
        var body = $"Для вашего заказа #{orderNumber} создан счет на оплату.{pdfLink}";
        
        return SendEmailAsync(email, subject, body);
    }

    public Task SendOrderCancellationNotificationAsync(string email, Guid orderId, string orderNumber, string? reason)
    {
        if (string.IsNullOrEmpty(email))
        {
            _logger.LogWarning("Не указан email для отправки уведомления об отмене заказа {OrderId}", orderId);
            return Task.CompletedTask;
        }

        var subject = $"Заказ #{orderNumber} отменен";
        var reasonText = !string.IsNullOrEmpty(reason) 
            ? $"<br><br>Причина отмены: {reason}" 
            : "";
        var body = $"Ваш заказ #{orderNumber} (ID: {orderId}) был отменен.{reasonText}";
        
        return SendEmailAsync(email, subject, body);
    }
}

