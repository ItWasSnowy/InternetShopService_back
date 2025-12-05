using System.Security.Claims;

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
}

