using System.Security.Claims;

namespace InternetShopService_back.Infrastructure.Jwt;

public interface IJwtTokenService
{
    /// <summary>
    /// Генерирует пару токенов (access и refresh) для пользователя
    /// </summary>
    (string AccessToken, string RefreshToken) GenerateTokens(Guid userId, string phoneNumber, Guid counterpartyId, Guid shopId);

    /// <summary>
    /// Валидирует access token
    /// </summary>
    ClaimsPrincipal? ValidateAccessToken(string token);

    /// <summary>
    /// Валидирует refresh token
    /// </summary>
    ClaimsPrincipal? ValidateRefreshToken(string token);

    /// <summary>
    /// Извлекает userId из токена
    /// </summary>
    Guid? GetUserIdFromToken(string token);
}

