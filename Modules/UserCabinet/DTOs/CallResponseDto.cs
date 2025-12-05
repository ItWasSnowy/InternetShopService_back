namespace InternetShopService_back.Modules.UserCabinet.DTOs;

public class CallResponseDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    
    /// <summary>
    /// Пин-код (последние 4 цифры номера звонящего)
    /// </summary>
    public string? LastFourDigits { get; set; }
    
    public string? RawResponse { get; set; }
    
    /// <summary>
    /// Флаг, указывающий на то, что превышен лимит звонков
    /// </summary>
    public bool IsCallLimitExceeded { get; set; }
    
    /// <summary>
    /// Оставшееся время ожидания в минутах до сброса лимита
    /// </summary>
    public int RemainingWaitTimeMinutes { get; set; }
}

