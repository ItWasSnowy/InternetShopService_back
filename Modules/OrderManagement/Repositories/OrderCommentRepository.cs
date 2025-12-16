using InternetShopService_back.Data;
using InternetShopService_back.Modules.OrderManagement.Models;
using Microsoft.EntityFrameworkCore;

namespace InternetShopService_back.Modules.OrderManagement.Repositories;

public class OrderCommentRepository : IOrderCommentRepository
{
    private readonly ApplicationDbContext _context;

    public OrderCommentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<OrderComment?> GetByIdAsync(Guid id)
    {
        return await _context.OrderComments
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<OrderComment?> GetByExternalCommentIdAsync(string externalCommentId)
    {
        return await _context.OrderComments
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.ExternalCommentId == externalCommentId);
    }

    public async Task<List<OrderComment>> GetByOrderIdAsync(Guid orderId)
    {
        return await _context.OrderComments
            .Include(c => c.Attachments)
            .Where(c => c.OrderId == orderId)
            .OrderBy(c => c.CreatedAt)
            .ToListAsync();
    }

    public async Task<OrderComment> CreateAsync(OrderComment comment)
    {
        comment.CreatedAt = DateTime.UtcNow;
        comment.UpdatedAt = DateTime.UtcNow;

        _context.OrderComments.Add(comment);
        await _context.SaveChangesAsync();

        return comment;
    }

    public async Task<OrderComment> UpdateAsync(OrderComment comment)
    {
        comment.UpdatedAt = DateTime.UtcNow;

        _context.OrderComments.Update(comment);
        await _context.SaveChangesAsync();

        return comment;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var comment = await _context.OrderComments
            .Include(c => c.Attachments)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (comment == null)
        {
            return false;
        }

        _context.OrderComments.Remove(comment);
        await _context.SaveChangesAsync();

        return true;
    }
}

