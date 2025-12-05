using InternetShopService_back.Data;
using InternetShopService_back.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Shared.Repositories;

public class ShopRepository : IShopRepository
{
    private readonly ApplicationDbContext _context;

    public ShopRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Shop?> GetByIdAsync(Guid id)
    {
        return await _context.Shops
            .FirstOrDefaultAsync(s => s.Id == id);
    }

    public async Task<Shop?> GetByFimBizCompanyIdAsync(int fimBizCompanyId, int? fimBizOrganizationId = null)
    {
        var query = _context.Shops
            .Where(s => s.FimBizCompanyId == fimBizCompanyId);

        if (fimBizOrganizationId.HasValue)
        {
            query = query.Where(s => s.FimBizOrganizationId == fimBizOrganizationId);
        }
        else
        {
            query = query.Where(s => s.FimBizOrganizationId == null);
        }

        return await query.FirstOrDefaultAsync();
    }

    public async Task<Shop?> GetByDomainAsync(string domain)
    {
        return await _context.Shops
            .FirstOrDefaultAsync(s => s.Domain == domain && s.IsActive);
    }

    public async Task<List<Shop>> GetAllActiveAsync()
    {
        return await _context.Shops
            .Where(s => s.IsActive)
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<List<Shop>> GetAllAsync()
    {
        return await _context.Shops
            .OrderBy(s => s.Name)
            .ToListAsync();
    }

    public async Task<Shop> CreateAsync(Shop shop)
    {
        shop.CreatedAt = DateTime.UtcNow;
        shop.UpdatedAt = DateTime.UtcNow;

        _context.Shops.Add(shop);
        await _context.SaveChangesAsync();

        return shop;
    }

    public async Task<Shop> UpdateAsync(Shop shop)
    {
        shop.UpdatedAt = DateTime.UtcNow;

        _context.Shops.Update(shop);
        await _context.SaveChangesAsync();

        return shop;
    }
}


