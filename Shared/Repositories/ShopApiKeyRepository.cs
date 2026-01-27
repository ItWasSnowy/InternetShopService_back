using InternetShopService_back.Data;
using InternetShopService_back.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Shared.Repositories;

public class ShopApiKeyRepository : IShopApiKeyRepository
{
    private readonly ApplicationDbContext _context;

    public ShopApiKeyRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public Task<ShopApiKey?> GetActiveByApiKeyAsync(string apiKey)
    {
        return _context.ShopApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ApiKey == apiKey && x.IsActive);
    }
}
