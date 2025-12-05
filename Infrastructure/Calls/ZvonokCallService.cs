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
        var formData = new MultipartFormDataContent
        {
            { new StringContent(_publicKey), "public_key" },
            { new StringContent(request.PhoneNumber), "phone" },
            { new StringContent(_campaignId), "campaign_id" }
        };

        var url = $"{_apiUrl}/phones/flashcall/";
        var response = await _httpClient.PostAsync(url, formData);
        var content = await response.Content.ReadAsStringAsync();

        _logger.LogDebug("Zvonok API ответ: {Response}", content);

        try
        {
            var responseObj = JsonNode.Parse(content);

            if (responseObj?["status"]?.GetValue<string>() == "error")
            {
                var errorMessage = responseObj["data"]?.GetValue<string>() ?? "Неизвестная ошибка";
                
                // Проверка на превышение лимита звонков
                if (errorMessage.Contains("excess limit") || errorMessage.Contains("limit"))
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
            if (response.IsSuccessStatusCode && responseObj != null)
            {
                var data = responseObj["data"];
                if (data != null && data["pincode"] != null)
                {
                    lastFourDigits = data["pincode"]?.GetValue<string>();
                }
            }

            return new CallResponseDto
            {
                Success = response.IsSuccessStatusCode,
                Message = response.IsSuccessStatusCode ? "Звонок успешно инициирован" : "Ошибка при отправке звонка",
                LastFourDigits = lastFourDigits,
                RawResponse = content
            };
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Ошибка парсинга ответа Zvonok API: {Content}", content);
            return new CallResponseDto
            {
                Success = false,
                Message = "Ошибка обработки ответа от сервиса звонков",
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
}

