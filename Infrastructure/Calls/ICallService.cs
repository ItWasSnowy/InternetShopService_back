using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Infrastructure.Calls;

public interface ICallService
{
    /// <summary>
    /// Отправляет flash-звонок на указанный номер телефона
    /// </summary>
    Task<CallResponseDto> SendCallAsync(CallRequestDto request);

    /// <summary>
    /// Отправляет звонок и автоматически обновляет данные пользователя 
    /// (последние 4 цифры и время звонка)
    /// </summary>
    Task<CallResponseDto> SendCallAndUpdateUserAsync(CallRequestDto request, Modules.UserCabinet.Models.UserAccount user);
}

