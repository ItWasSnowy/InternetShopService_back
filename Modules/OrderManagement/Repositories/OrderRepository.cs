using InternetShopService_back.Data;
using InternetShopService_back.Modules.OrderManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.OrderManagement.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _context;

    public OrderRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<Order?> GetByIdAsync(Guid id)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.CargoReceiver)
            .Include(o => o.Attachments)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order?> GetByFimBizOrderIdAsync(int fimBizOrderId)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.CargoReceiver)
            .Include(o => o.Attachments)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.FimBizOrderId == fimBizOrderId);
    }

    public async Task<List<Order>> GetByUserIdAsync(Guid userId)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.CargoReceiver)
            .Include(o => o.StatusHistory)
            .Where(o => o.UserAccountId == userId)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<(List<Order> Orders, int TotalCount)> GetByUserIdPagedAsync(Guid userId, int page, int pageSize)
    {
        var query = _context.Orders
            .Where(o => o.UserAccountId == userId);

        var totalCount = await query.CountAsync();

        var orders = await query
            .Include(o => o.Items)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.CargoReceiver)
            .Include(o => o.StatusHistory)
            .AsSplitQuery() // Разделяем запрос на несколько SQL запросов для лучшей производительности
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (orders, totalCount);
    }

    public async Task<Order> CreateAsync(Order order)
    {
        order.CreatedAt = DateTime.UtcNow;
        order.UpdatedAt = DateTime.UtcNow;

        if (string.IsNullOrEmpty(order.OrderNumber))
        {
            order.OrderNumber = await GenerateOrderNumberAsync();
        }

        _context.Orders.Add(order);
        await _context.SaveChangesAsync();

        return order;
    }

    public async Task<Order> UpdateAsync(Order order)
    {
        order.UpdatedAt = DateTime.UtcNow;

        _context.Orders.Update(order);
        await _context.SaveChangesAsync();

        return order;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.StatusHistory)
            .Include(o => o.Attachments)
            .FirstOrDefaultAsync(o => o.Id == id);
        
        if (order == null)
            return false;

        _context.Orders.Remove(order);
        await _context.SaveChangesAsync();
        
        return true;
    }

    public async Task<string> GenerateOrderNumberAsync()
    {
        var today = DateTime.UtcNow.Date;
        var year = today.Year;
        var month = today.Month;

        // Получаем последний номер заказа за текущий месяц
        var lastOrder = await _context.Orders
            .Where(o => o.CreatedAt.Year == year && o.CreatedAt.Month == month)
            .OrderByDescending(o => o.OrderNumber)
            .FirstOrDefaultAsync();

        int nextNumber = 1;
        if (lastOrder != null && !string.IsNullOrEmpty(lastOrder.OrderNumber))
        {
            // Пытаемся извлечь номер из строки формата "ORD-YYYY-MM-XXXX"
            var parts = lastOrder.OrderNumber.Split('-');
            if (parts.Length == 4 && int.TryParse(parts[3], out var lastNumber))
            {
                nextNumber = lastNumber + 1;
            }
        }

        return $"ORD-{year}-{month:D2}-{nextNumber:D4}";
    }

    public async Task<List<Order>> GetUnsyncedOrdersAsync(int limit = 100)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.UserAccount)
            .ThenInclude(u => u.Counterparty)
            .Where(o => o.FimBizOrderId == null || o.SyncedWithFimBizAt == null)
            .OrderBy(o => o.CreatedAt) // Старые заказы в первую очередь
            .Take(limit)
            .ToListAsync();
    }
}

