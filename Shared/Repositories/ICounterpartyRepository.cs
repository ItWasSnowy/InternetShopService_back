using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Shared.Repositories;

public interface ICounterpartyRepository
{
    Task<Counterparty?> GetByIdAsync(Guid id);
    Task<Counterparty?> GetByPhoneNumberAsync(string phoneNumber);
    Task<Counterparty?> GetByFimBizIdAsync(int fimBizContractorId);
    Task<List<Discount>> GetActiveDiscountsAsync(Guid counterpartyId);
    Task<Counterparty> CreateAsync(Counterparty counterparty);
    Task<Counterparty> UpdateAsync(Counterparty counterparty);
}

