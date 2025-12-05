using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Shared.Repositories;

public interface IShopRepository
{
    Task<Shop?> GetByIdAsync(Guid id);
    Task<Shop?> GetByFimBizCompanyIdAsync(int fimBizCompanyId, int? fimBizOrganizationId = null);
    Task<Shop?> GetByDomainAsync(string domain);
    Task<List<Shop>> GetAllActiveAsync();
    Task<List<Shop>> GetAllAsync();
    Task<Shop> CreateAsync(Shop shop);
    Task<Shop> UpdateAsync(Shop shop);
}


