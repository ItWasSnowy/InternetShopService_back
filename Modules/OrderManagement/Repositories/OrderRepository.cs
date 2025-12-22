using InternetShopService_back.Data;
using InternetShopService_back.Modules.OrderManagement.Models;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .Include(o => o.DeliveryAddress)
            .Include(o => o.CargoReceiver)
            .Include(o => o.Attachments)
            .Include(o => o.StatusHistory)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
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

        const int maxRetries = 3;
        for (var attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                await _context.SaveChangesAsync();
                break;
            }
            catch (DbUpdateException ex) when (
                ex.InnerException is PostgresException pgEx &&
                pgEx.SqlState == "23505" &&
                pgEx.ConstraintName == "IX_Orders_OrderNumber" &&
                attempt < maxRetries)
            {
                order.OrderNumber = await GenerateOrderNumberAsync();
            }
        }

        return order;
    }

    public async Task<Order> UpdateAsync(Order order)
    {
        order.UpdatedAt = DateTime.UtcNow;

        // Используем Entry API для безопасного обновления, чтобы избежать DbUpdateConcurrencyException
        var entry = _context.Entry(order);
        
        if (entry.State == EntityState.Detached)
        {
            // Если заказ не отслеживается, загружаем его из БД без навигационных свойств
            var existingOrder = await _context.Orders.FindAsync(order.Id);
            if (existingOrder == null)
            {
                throw new InvalidOperationException($"Заказ с ID {order.Id} не найден в базе данных");
            }
            
            // Обновляем только скалярные свойства через Entry API
            var existingEntry = _context.Entry(existingOrder);
            existingEntry.Property(o => o.Status).CurrentValue = order.Status;
            existingEntry.Property(o => o.OrderNumber).CurrentValue = order.OrderNumber;
            existingEntry.Property(o => o.TotalAmount).CurrentValue = order.TotalAmount;
            existingEntry.Property(o => o.TrackingNumber).CurrentValue = order.TrackingNumber;
            existingEntry.Property(o => o.Carrier).CurrentValue = order.Carrier;
            existingEntry.Property(o => o.DeliveryType).CurrentValue = order.DeliveryType;
            existingEntry.Property(o => o.IsPriority).CurrentValue = order.IsPriority;
            existingEntry.Property(o => o.IsLongAssembling).CurrentValue = order.IsLongAssembling;
            existingEntry.Property(o => o.FimBizOrderId).CurrentValue = order.FimBizOrderId;
            existingEntry.Property(o => o.InvoiceId).CurrentValue = order.InvoiceId;
            existingEntry.Property(o => o.UpdDocumentId).CurrentValue = order.UpdDocumentId;
            existingEntry.Property(o => o.AssembledAt).CurrentValue = order.AssembledAt;
            existingEntry.Property(o => o.ShippedAt).CurrentValue = order.ShippedAt;
            existingEntry.Property(o => o.DeliveredAt).CurrentValue = order.DeliveredAt;
            existingEntry.Property(o => o.AssemblerId).CurrentValue = order.AssemblerId;
            existingEntry.Property(o => o.DriverId).CurrentValue = order.DriverId;
            existingEntry.Property(o => o.SyncedWithFimBizAt).CurrentValue = order.SyncedWithFimBizAt;
            existingEntry.Property(o => o.UpdatedAt).CurrentValue = order.UpdatedAt;
            
            // Помечаем только измененные свойства как Modified
            existingEntry.Property(o => o.Status).IsModified = true;
            existingEntry.Property(o => o.OrderNumber).IsModified = true;
            existingEntry.Property(o => o.TotalAmount).IsModified = true;
            existingEntry.Property(o => o.TrackingNumber).IsModified = true;
            existingEntry.Property(o => o.Carrier).IsModified = true;
            existingEntry.Property(o => o.DeliveryType).IsModified = true;
            existingEntry.Property(o => o.IsPriority).IsModified = true;
            existingEntry.Property(o => o.IsLongAssembling).IsModified = true;
            existingEntry.Property(o => o.FimBizOrderId).IsModified = true;
            existingEntry.Property(o => o.InvoiceId).IsModified = true;
            existingEntry.Property(o => o.UpdDocumentId).IsModified = true;
            existingEntry.Property(o => o.AssembledAt).IsModified = true;
            existingEntry.Property(o => o.ShippedAt).IsModified = true;
            existingEntry.Property(o => o.DeliveredAt).IsModified = true;
            existingEntry.Property(o => o.AssemblerId).IsModified = true;
            existingEntry.Property(o => o.DriverId).IsModified = true;
            existingEntry.Property(o => o.SyncedWithFimBizAt).IsModified = true;
            existingEntry.Property(o => o.UpdatedAt).IsModified = true;
            
            // Сохраняем новые записи в истории статусов, если они были добавлены
            if (order.StatusHistory != null && order.StatusHistory.Count > 0)
            {
                // Получаем текущие записи из БД
                var existingHistoryIds = new HashSet<Guid>(await _context.OrderStatusHistories
                    .Where(h => h.OrderId == order.Id)
                    .Select(h => h.Id)
                    .ToListAsync());
                
                // Находим новые записи, которых нет в БД
                var newHistoryEntries = order.StatusHistory
                    .Where(h => !existingHistoryIds.Contains(h.Id))
                    .ToList();
                
                // Добавляем новые записи в контекст
                foreach (var historyEntry in newHistoryEntries)
                {
                    _context.OrderStatusHistories.Add(historyEntry);
                }
            }
            
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                // Обработка нарушения уникальности
                if (pgEx.ConstraintName == "IX_Orders_OrderNumber")
                {
                    throw new DbUpdateException(
                        $"Нарушение уникальности OrderNumber: номер заказа '{order.OrderNumber}' уже используется другим заказом. " +
                        $"Constraint: {pgEx.ConstraintName}",
                        ex);
                }
                throw;
            }
            
            return existingOrder;
        }
        else
        {
            // Если заказ уже отслеживается, проверяем его существование в БД перед обновлением
            var exists = await _context.Orders.AnyAsync(o => o.Id == order.Id);
            if (!exists)
            {
                throw new InvalidOperationException($"Заказ с ID {order.Id} не найден в базе данных. Возможно, он был удалён другим процессом.");
            }
            
            // Обновляем значения свойств из переданного объекта
            // Используем селективное обновление свойств вместо EntityState.Modified
            // Это позволяет избежать проблем с конкурентным доступом
            entry.Property(o => o.Status).CurrentValue = order.Status;
            entry.Property(o => o.OrderNumber).CurrentValue = order.OrderNumber;
            entry.Property(o => o.TotalAmount).CurrentValue = order.TotalAmount;
            entry.Property(o => o.TrackingNumber).CurrentValue = order.TrackingNumber;
            entry.Property(o => o.Carrier).CurrentValue = order.Carrier;
            entry.Property(o => o.DeliveryType).CurrentValue = order.DeliveryType;
            entry.Property(o => o.IsPriority).CurrentValue = order.IsPriority;
            entry.Property(o => o.IsLongAssembling).CurrentValue = order.IsLongAssembling;
            entry.Property(o => o.FimBizOrderId).CurrentValue = order.FimBizOrderId;
            entry.Property(o => o.InvoiceId).CurrentValue = order.InvoiceId;
            entry.Property(o => o.UpdDocumentId).CurrentValue = order.UpdDocumentId;
            entry.Property(o => o.AssembledAt).CurrentValue = order.AssembledAt;
            entry.Property(o => o.ShippedAt).CurrentValue = order.ShippedAt;
            entry.Property(o => o.DeliveredAt).CurrentValue = order.DeliveredAt;
            entry.Property(o => o.AssemblerId).CurrentValue = order.AssemblerId;
            entry.Property(o => o.DriverId).CurrentValue = order.DriverId;
            entry.Property(o => o.SyncedWithFimBizAt).CurrentValue = order.SyncedWithFimBizAt;
            entry.Property(o => o.UpdatedAt).CurrentValue = order.UpdatedAt;
            
            // Помечаем свойства как измененные
            entry.Property(o => o.Status).IsModified = true;
            entry.Property(o => o.OrderNumber).IsModified = true;
            entry.Property(o => o.TotalAmount).IsModified = true;
            entry.Property(o => o.TrackingNumber).IsModified = true;
            entry.Property(o => o.Carrier).IsModified = true;
            entry.Property(o => o.DeliveryType).IsModified = true;
            entry.Property(o => o.IsPriority).IsModified = true;
            entry.Property(o => o.IsLongAssembling).IsModified = true;
            entry.Property(o => o.FimBizOrderId).IsModified = true;
            entry.Property(o => o.InvoiceId).IsModified = true;
            entry.Property(o => o.UpdDocumentId).IsModified = true;
            entry.Property(o => o.AssembledAt).IsModified = true;
            entry.Property(o => o.ShippedAt).IsModified = true;
            entry.Property(o => o.DeliveredAt).IsModified = true;
            entry.Property(o => o.AssemblerId).IsModified = true;
            entry.Property(o => o.DriverId).IsModified = true;
            entry.Property(o => o.SyncedWithFimBizAt).IsModified = true;
            entry.Property(o => o.UpdatedAt).IsModified = true;
            
            // Сохраняем новые записи в истории статусов, если они были добавлены
            if (order.StatusHistory != null && order.StatusHistory.Count > 0)
            {
                // Получаем текущие записи из БД
                var existingHistoryIds = new HashSet<Guid>(await _context.OrderStatusHistories
                    .Where(h => h.OrderId == order.Id)
                    .Select(h => h.Id)
                    .ToListAsync());
                
                // Находим новые записи, которых нет в БД
                var newHistoryEntries = order.StatusHistory
                    .Where(h => !existingHistoryIds.Contains(h.Id))
                    .ToList();
                
                // Добавляем новые записи в контекст
                foreach (var historyEntry in newHistoryEntries)
                {
                    _context.OrderStatusHistories.Add(historyEntry);
                }
            }
            
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException pgEx && pgEx.SqlState == "23505")
            {
                // Обработка нарушения уникальности
                if (pgEx.ConstraintName == "IX_Orders_OrderNumber")
                {
                    throw new DbUpdateException(
                        $"Нарушение уникальности OrderNumber: номер заказа '{order.OrderNumber}' уже используется другим заказом. " +
                        $"Constraint: {pgEx.ConstraintName}",
                        ex);
                }
                throw;
            }
            
            return order;
        }
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

        var prefix = $"ORD-{year}-{month:D2}-";

        // Получаем последний номер заказа за текущий месяц
        var lastOrder = await _context.Orders
            .Where(o => o.OrderNumber.StartsWith(prefix))
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

        return $"{prefix}{nextNumber:D4}";
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

