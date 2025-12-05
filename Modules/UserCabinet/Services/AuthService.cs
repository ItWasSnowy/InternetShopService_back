using InternetShopService_back.Modules.UserCabinet.DTOs;

namespace InternetShopService_back.Modules.UserCabinet.Services;

public class AuthService : IAuthService
{
    public Task<AuthResponseDto> RequestPhoneCodeAsync(string phoneNumber)
    {
        throw new NotImplementedException();
    }

    public Task<AuthResponseDto> VerifyCodeAsync(string phoneNumber, string code)
    {
        throw new NotImplementedException();
    }

    public Task<AuthResponseDto> SetPasswordAsync(string phoneNumber, string password)
    {
        throw new NotImplementedException();
    }

    public Task<AuthResponseDto> LoginByPasswordAsync(string phoneNumber, string password)
    {
        throw new NotImplementedException();
    }

    public Task<bool> ValidateTokenAsync(string token)
    {
        throw new NotImplementedException();
    }

    public Task LogoutAsync(string token)
    {
        throw new NotImplementedException();
    }
}

