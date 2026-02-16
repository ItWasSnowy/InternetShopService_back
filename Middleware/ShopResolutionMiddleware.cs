using InternetShopService_back.Shared.Repositories;
using InternetShopService_back.Shared.Services;
using Microsoft.Extensions.Logging;

namespace InternetShopService_back.Middleware;

public class ShopResolutionMiddleware
{
    private readonly RequestDelegate _next;

    public ShopResolutionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IShopRepository shopRepository,
        IShopContext shopContext,
        ILogger<ShopResolutionMiddleware> logger)
    {
        if (!shopContext.ShopId.HasValue)
        {
            var domain = ResolveDomain(context);
            if (!string.IsNullOrWhiteSpace(domain))
            {
                var shop = await shopRepository.GetByDomainAsync(domain);
                if (shop != null)
                {
                    shopContext.SetShopId(shop.Id);
                    context.Items["ShopId"] = shop.Id;
                }
                else
                {
                    logger.LogDebug("Shop not resolved for domain {Domain}", domain);
                }
            }
        }

        await _next(context);
    }

    private static string? ResolveDomain(HttpContext context)
    {
        var origin = context.Request.Headers.Origin.ToString();
        if (!string.IsNullOrWhiteSpace(origin) && Uri.TryCreate(origin, UriKind.Absolute, out var originUri))
        {
            return NormalizeHost(originUri.Host);
        }

        var forwardedHost = context.Request.Headers["X-Forwarded-Host"].ToString();
        if (!string.IsNullOrWhiteSpace(forwardedHost))
        {
            var host = forwardedHost.Split(',')[0].Trim();
            return NormalizeHost(host);
        }

        var requestHost = context.Request.Host.Host;
        if (!string.IsNullOrWhiteSpace(requestHost))
        {
            return NormalizeHost(requestHost);
        }

        return null;
    }

    private static string NormalizeHost(string host)
    {
        return host.Trim().TrimEnd('.').ToLowerInvariant();
    }
}

public static class ShopResolutionMiddlewareExtensions
{
    public static IApplicationBuilder UseShopResolution(this IApplicationBuilder builder)
    {
        return builder.UseMiddleware<ShopResolutionMiddleware>();
    }
}
