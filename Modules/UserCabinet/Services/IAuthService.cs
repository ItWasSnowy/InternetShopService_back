using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public interface IAuthService
{
    Task<AuthResponseDto> RequestPhoneCodeAsync(string phoneNumber);
    Task<AuthResponseDto> VerifyCodeAsync(string phoneNumber, string code);
    Task<AuthResponseDto> SetPasswordAsync(string phoneNumber, string password);
    Task<AuthResponseDto> LoginByPasswordAsync(string phoneNumber, string password);
    Task<bool> ValidateTokenAsync(string token);
    Task LogoutAsync(string token);
}

