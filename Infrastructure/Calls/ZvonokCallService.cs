using System.Text.Json;
using System.Text.Json.Nodes;
using InternetShopService_back.Modules.UserCabinet.DTOs;
using InternetShopService_back.Modules.UserCabinet.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Infrastructure.Calls;

public class ZvonokCallService : ICallService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ZvonokCallService> _logger;
    private readonly string _publicKey;
    private readonly string _apiUrl;
    private readonly string _campaignId;

    public ZvonokCallService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<ZvonokCallService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
        
        _publicKey = _configuration["ZvonokAPI:PublicKey"] ?? throw new InvalidOperationException("ZvonokAPI:PublicKey не настроен");
        _apiUrl = _configuration["ZvonokAPI:AddFlashCallUrl"] ?? "https://zvonok.com/api";
        _campaignId = _configuration["ZvonokAPI:CampaignId"] ?? throw new InvalidOperationException("ZvonokAPI:CampaignId не настроен");
    }

    public async Task<CallResponseDto> SendCallAsync(CallRequestDto request)
    {
        try
        {
            return await SendFlashCallAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке звонка на номер {PhoneNumber}", request.PhoneNumber);
            return new CallResponseDto
            {
                Success = false,
                Message = $"Ошибка при отправке звонка: {ex.Message}",
                LastFourDigits = null,
                RawResponse = ex.Message
            };
        }
    }

    public async Task<CallResponseDto> SendCallAndUpdateUserAsync(CallRequestDto request, UserAccount user)
    {
        try
        {
            var callResult = await SendFlashCallAsync(request);

            if (callResult.Success && !string.IsNullOrEmpty(callResult.LastFourDigits))
            {
                // Обновление данных пользователя
                user.PhoneCallDigits = callResult.LastFourDigits;
                user.PhoneCallDateTime = DateTime.UtcNow;

                _logger.LogInformation("Звонок успешно отправлен на номер {PhoneNumber}, код: {Code}", 
                    request.PhoneNumber, callResult.LastFourDigits);
            }

            return callResult;
        }
        catch (InvalidOperationException ex)
        {
            return new CallResponseDto
            {
                Success = false,
                Message = ex.Message,
                LastFourDigits = null,
                RawResponse = ex.Message,
                IsCallLimitExceeded = true
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при отправке звонка и обновлении пользователя {UserId}", user.Id);
            return new CallResponseDto
            {
                Success = false,
                Message = $"Ошибка при отправке звонка: {ex.Message}",
                LastFourDigits = null,
                RawResponse = ex.Message
            };
        }
    }

    private async Task<CallResponseDto> SendFlashCallAsync(CallRequestDto request)
    {
        // Нормализация номера телефона для Zvonok API (формат: 7XXXXXXXXXX)
        var normalizedPhone = NormalizePhoneNumber(request.PhoneNumber);
        
        _logger.LogDebug("Нормализованный номер телефона: {NormalizedPhone} (исходный: {OriginalPhone})", 
            normalizedPhone, request.PhoneNumber);
        
        // Используем POST запрос с MultipartFormDataContent (как в FimBiz)
        var formData = new MultipartFormDataContent
        {
            { new StringContent(_publicKey), "public_key" },
            { new StringContent(normalizedPhone), "phone" },
            { new StringContent(_campaignId), "campaign_id" }
        };

        // Формируем правильный URL согласно документации FimBiz
        var url = $"{_apiUrl}/phones/flashcall/";
        _logger.LogInformation("Отправка POST запроса к Zvonok API: {Url}, номер: {PhoneNumber}, campaign_id: {CampaignId}", 
            url, normalizedPhone, _campaignId);
        
        var response = await _httpClient.PostAsync(url, formData);
        var content = await response.Content.ReadAsStringAsync();

        var contentPreview = string.IsNullOrEmpty(content) 
            ? "пусто" 
            : content.Substring(0, Math.Min(500, content.Length));
        
        _logger.LogInformation("Zvonok API ответ от {Url}: StatusCode={StatusCode}, Content={Response}", 
            url, response.StatusCode, contentPreview);

        // Проверка на HTTP ошибки
        if (!response.IsSuccessStatusCode)
        {
            var errorMsg = $"HTTP {response.StatusCode}: {content}";
            _logger.LogError("Zvonok API вернул HTTP ошибку: {Error}", errorMsg);
            return new CallResponseDto
            {
                Success = false,
                Message = errorMsg,
                LastFourDigits = null,
                RawResponse = content
            };
        }

        try
        {
            // Пытаемся распарсить JSON ответ
            JsonNode? responseObj = null;
            try
            {
                if (string.IsNullOrEmpty(content))
                {
                    return new CallResponseDto
                    {
                        Success = false,
                        Message = "Пустой ответ от сервиса звонков",
                        LastFourDigits = null,
                        RawResponse = content ?? string.Empty
                    };
                }
                
                responseObj = JsonNode.Parse(content);
            }
            catch (JsonException jsonEx)
            {
                _logger.LogWarning(jsonEx, "Ответ не является валидным JSON: {Content}", content);
                // Возможно, ответ в другом формате, попробуем обработать как текст
                if (content.Contains("success") || content.Contains("error"))
                {
                    // Попытка найти код в текстовом ответе
                    var match = System.Text.RegularExpressions.Regex.Match(content, @"(\d{4})");
                    if (match.Success)
                    {
                        return new CallResponseDto
                        {
                            Success = true,
                            Message = "Звонок успешно инициирован",
                            LastFourDigits = match.Groups[1].Value,
                            RawResponse = content
                        };
                    }
                }
                
                return new CallResponseDto
                {
                    Success = false,
                    Message = $"Не удалось обработать ответ от сервиса: {content}",
                    LastFourDigits = null,
                    RawResponse = content
                };
            }

            if (responseObj == null)
            {
                _logger.LogWarning("Ответ от Zvonok API пустой или null");
                return new CallResponseDto
                {
                    Success = false,
                    Message = "Пустой ответ от сервиса звонков",
                    LastFourDigits = null,
                    RawResponse = content
                };
            }

            // Проверка на ошибку в ответе
            var status = responseObj["status"]?.GetValue<string>();
            var success = responseObj["success"]?.GetValue<string>();
            
            if (status == "error" || success == "error")
            {
                var errorMessage = responseObj["data"]?.GetValue<string>() 
                    ?? responseObj["message"]?.GetValue<string>() 
                    ?? responseObj["error"]?.GetValue<string>() 
                    ?? "Неизвестная ошибка";
                
                _logger.LogWarning("Zvonok API вернул ошибку: {Error}", errorMessage);
                
                // Проверка на превышение лимита звонков
                if (errorMessage.Contains("excess limit") || errorMessage.Contains("limit") || 
                    errorMessage.Contains("лимит") || errorMessage.Contains("превышен"))
                {
                    // Извлечение времени ожидания (если есть)
                    int waitTime = ExtractWaitTimeFromError(errorMessage);
                    
                    return new CallResponseDto
                    {
                        Success = false,
                        Message = $"Превышен лимит звонков. {errorMessage}",
                        LastFourDigits = null,
                        RawResponse = content,
                        IsCallLimitExceeded = true,
                        RemainingWaitTimeMinutes = waitTime
                    };
                }

                return new CallResponseDto
                {
                    Success = false,
                    Message = errorMessage,
                    LastFourDigits = null,
                    RawResponse = content
                };
            }

            // Извлечение пин-кода из ответа
            string? lastFourDigits = null;
            
            // Пробуем разные варианты структуры ответа
            var data = responseObj["data"];
            if (data != null)
            {
                // Вариант 1: data.pincode
                if (data["pincode"] != null)
                {
                    lastFourDigits = data["pincode"]?.GetValue<string>();
                }
                // Вариант 2: data.code
                else if (data["code"] != null)
                {
                    lastFourDigits = data["code"]?.GetValue<string>();
                }
                // Вариант 3: data напрямую строка с кодом
                else if (data.GetValueKind() == System.Text.Json.JsonValueKind.String)
                {
                    var codeStr = data.GetValue<string>();
                    if (codeStr != null && codeStr.Length == 4 && System.Text.RegularExpressions.Regex.IsMatch(codeStr, @"^\d{4}$"))
                    {
                        lastFourDigits = codeStr;
                    }
                }
            }
            
            // Если не нашли в data, ищем в корне ответа
            if (string.IsNullOrEmpty(lastFourDigits))
            {
                if (responseObj["pincode"] != null)
                {
                    lastFourDigits = responseObj["pincode"]?.GetValue<string>();
                }
                else if (responseObj["code"] != null)
                {
                    lastFourDigits = responseObj["code"]?.GetValue<string>();
                }
            }

            _logger.LogInformation("Обработка ответа завершена. Success={Success}, Code={Code}", 
                response.IsSuccessStatusCode, lastFourDigits ?? "не найден");

            return new CallResponseDto
            {
                Success = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode 
                    ? (string.IsNullOrEmpty(lastFourDigits) 
                        ? "Звонок инициирован, но код не получен" 
                        : "Звонок успешно инициирован")
                    : "Ошибка при отправке звонка",
                LastFourDigits = lastFourDigits,
                RawResponse = content
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка при обработке ответа Zvonok API: {Content}", content);
            return new CallResponseDto
            {
                Success = false,
                Message = $"Ошибка обработки ответа от сервиса звонков: {ex.Message}",
                LastFourDigits = null,
                RawResponse = content
            };
        }
    }

    private int ExtractWaitTimeFromError(string errorMessage)
    {
        // Попытка извлечь время ожидания из сообщения об ошибке
        // Например: "limit 1/5sec" означает 5 секунд = 0 минут
        // Или: "limit 1/60sec" означает 60 секунд = 1 минута
        var match = System.Text.RegularExpressions.Regex.Match(errorMessage, @"(\d+)\s*sec");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int seconds))
        {
            return (int)Math.Ceiling(seconds / 60.0); // Конвертируем секунды в минуты
        }
        return 5; // По умолчанию 5 минут
    }

    private string NormalizePhoneNumber(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber))
            return string.Empty;

        // Убираем все нецифровые символы, кроме + в начале
        var normalized = phoneNumber.Replace(" ", "")
            .Replace("-", "")
            .Replace("(", "")
            .Replace(")", "");

        // Если начинается с +7, убираем +
        if (normalized.StartsWith("+7"))
            normalized = normalized.Substring(1);

        // Если начинается с 8, заменяем на 7
        if (normalized.StartsWith("8"))
            normalized = "7" + normalized.Substring(1);

        // Убеждаемся, что начинается с 7
        if (!normalized.StartsWith("7"))
            normalized = "7" + normalized;

        return normalized;
    }
}

