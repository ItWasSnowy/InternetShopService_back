using InternetShopService_back.Data;
using InternetShopService_back.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Shared.Repositories;

public class CounterpartyRepository : ICounterpartyRepository
{
    private readonly ApplicationDbContext _context;

    public CounterpartyRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Counterparty?> GetByIdAsync(Guid id)
    {
        return await _context.Counterparties
            .Include(c => c.Discounts)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Counterparty?> GetByPhoneNumberAsync(string phoneNumber)
    {
        return await _context.Counterparties
            .Include(c => c.Discounts)
            .FirstOrDefaultAsync(c => c.PhoneNumber == phoneNumber);
    }

    public async Task<Counterparty?> GetByFimBizIdAsync(int fimBizContractorId)
    {
        return await _context.Counterparties
            .Include(c => c.Discounts)
            .FirstOrDefaultAsync(c => c.FimBizContractorId == fimBizContractorId);
    }

    public async Task<List<Discount>> GetActiveDiscountsAsync(Guid counterpartyId)
    {
        var now = DateTime.UtcNow;
        return await _context.Discounts
            .Where(d => d.CounterpartyId == counterpartyId
                && d.IsActive
                && d.ValidFrom <= now
                && (d.ValidTo == null || d.ValidTo >= now))
            .OrderByDescending(d => d.DiscountPercent)
            .ToListAsync();
    }

    public async Task<Counterparty> CreateAsync(Counterparty counterparty)
    {
        counterparty.CreatedAt = DateTime.UtcNow;
        counterparty.UpdatedAt = DateTime.UtcNow;
        
        _context.Counterparties.Add(counterparty);
        await _context.SaveChangesAsync();
        
        return counterparty;
    }

    public async Task<Counterparty> UpdateAsync(Counterparty counterparty)
    {
        counterparty.UpdatedAt = DateTime.UtcNow;
        
        _context.Counterparties.Update(counterparty);
        await _context.SaveChangesAsync();
        
        return counterparty;
    }
}

