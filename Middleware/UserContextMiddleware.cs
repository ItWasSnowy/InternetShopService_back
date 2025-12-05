using System.Security.Claims;
using InternetShopService_back.Infrastructure.Jwt;

namespace InternetShopService_back.Middleware;

public class UserContextMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IJwtTokenService _jwtTokenService;

    public UserContextMiddleware(RequestDelegate next, IJwtTokenService jwtTokenService)
    {
        _next = next;
        _jwtTokenService = jwtTokenService;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated == true)
        {
            var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
            if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var userId))
            {
                context.Items["UserId"] = userId;
            }

            var counterpartyIdClaim = context.User.FindFirst("CounterpartyId");
            if (counterpartyIdClaim != null && Guid.TryParse(counterpartyIdClaim.Value, out var counterpartyId))
            {
                context.Items["CounterpartyId"] = counterpartyId;
            }
        }

        await _next(context);
    }
}

public static class UserContextMiddlewareExtensions
{
    public static IApplicationBuilder UseUserContext(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<UserContextMiddleware>();
    }
}

