using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Shared.Repositories;

public interface IShopApiKeyRepository
{
    Task<ShopApiKey?> GetActiveByApiKeyAsync(string apiKey);
}
