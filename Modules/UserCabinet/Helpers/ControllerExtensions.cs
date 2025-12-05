using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;

namespace InternetShopService_back.Modules.UserCabinet.Helpers;

public static class ControllerExtensions
{
    public static Guid? GetUserId(this HttpContext context)
    {
        if (context.Items.TryGetValue("UserId", out var userId) && userId is Guid guid)
        {
            return guid;
        }

        var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
        if (userIdClaim != null && Guid.TryParse(userIdClaim.Value, out var parsedUserId))
        {
            return parsedUserId;
        }

        return null;
    }

    public static Guid? GetCounterpartyId(this HttpContext context)
    {
        if (context.Items.TryGetValue("CounterpartyId", out var counterpartyId) && counterpartyId is Guid guid)
        {
            return guid;
        }

        var counterpartyIdClaim = context.User.FindFirst("CounterpartyId");
        if (counterpartyIdClaim != null && Guid.TryParse(counterpartyIdClaim.Value, out var parsedCounterpartyId))
        {
            return parsedCounterpartyId;
        }

        return null;
    }

    public static string? GetPhoneNumber(this HttpContext context)
    {
        // Пытаемся получить из Items (установлено middleware)
        if (context.Items.TryGetValue("PhoneNumber", out var phoneNumber) && phoneNumber is string phone)
        {
            return phone;
        }

        // Получаем из JWT claims
        var phoneClaim = context.User.FindFirst(ClaimTypes.MobilePhone) 
                      ?? context.User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/mobilephone");
        if (phoneClaim != null)
        {
            return phoneClaim.Value;
        }

        return null;
    }

    // Extension для ControllerBase
    public static string? GetPhoneNumber(this ControllerBase controller)
    {
        return GetPhoneNumber(controller.HttpContext);
    }

    public static Guid? GetShopId(this HttpContext context)
    {
        if (context.Items.TryGetValue("ShopId", out var shopId) && shopId is Guid guid)
        {
            return guid;
        }

        var shopIdClaim = context.User.FindFirst("ShopId");
        if (shopIdClaim != null && Guid.TryParse(shopIdClaim.Value, out var parsedShopId))
        {
            return parsedShopId;
        }

        return null;
    }

    public static Guid? GetShopId(this ControllerBase controller)
    {
        return GetShopId(controller.HttpContext);
    }
}

