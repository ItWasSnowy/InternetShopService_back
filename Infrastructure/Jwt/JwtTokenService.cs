using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace InternetShopService_back.Infrastructure.Jwt;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _configuration;
    private readonly string _secretKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expirationMinutes;

    public JwtTokenService(IConfiguration configuration)
    {
        _configuration = configuration;
        _secretKey = _configuration["JwtSettings:SecretKey"] 
            ?? throw new InvalidOperationException("JwtSettings:SecretKey не настроен");
        _issuer = _configuration["JwtSettings:Issuer"] ?? "InternetShopService";
        _audience = _configuration["JwtSettings:Audience"] ?? "InternetShopService";
        _expirationMinutes = _configuration.GetValue<int>("JwtSettings:ExpirationMinutes", 60);
    }

    public (string AccessToken, string RefreshToken) GenerateTokens(Guid userId, string phoneNumber, Guid counterpartyId)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.MobilePhone, phoneNumber),
            new Claim("CounterpartyId", counterpartyId.ToString()),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var accessToken = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expirationMinutes),
            signingCredentials: credentials
        );

        var refreshToken = GenerateRefreshToken();

        var accessTokenString = new JwtSecurityTokenHandler().WriteToken(accessToken);

        return (accessTokenString, refreshToken);
    }

    public ClaimsPrincipal? ValidateAccessToken(string token)
    {
        return ValidateToken(token);
    }

    public ClaimsPrincipal? ValidateRefreshToken(string token)
    {
        // Refresh token валидируется по-другому (хранится в БД)
        // Здесь можно добавить проверку формата
        return null;
    }

    public Guid? GetUserIdFromToken(string token)
    {
        var principal = ValidateAccessToken(token);
        if (principal == null)
            return null;

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !Guid.TryParse(userIdClaim.Value, out var userId))
            return null;

        return userId;
    }

    private ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_secretKey);

            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = _issuer,
                ValidateAudience = true,
                ValidAudience = _audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };

            var principal = tokenHandler.ValidateToken(token, validationParameters, out _);
            return principal;
        }
        catch
        {
            return null;
        }
    }

    private string GenerateRefreshToken()
    {
        var randomNumber = new byte[64];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(randomNumber);
        return Convert.ToBase64String(randomNumber);
    }
}

