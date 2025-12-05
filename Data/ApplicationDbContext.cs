using Microsoft.EntityFrameworkCore;
using InternetShopService_back.Shared.Models;
using InternetShopService_back.Modules.UserCabinet.Models;
using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    // Shared
    public DbSet<Counterparty> Counterparties { get; set; }
    public DbSet<Discount> Discounts { get; set; }

    // UserCabinet Module
    public DbSet<UserAccount> UserAccounts { get; set; }
    public DbSet<Cart> Carts { get; set; }
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<DeliveryAddress> DeliveryAddresses { get; set; }
    public DbSet<CargoReceiver> CargoReceivers { get; set; }
    public DbSet<Session> Sessions { get; set; }

    // OrderManagement Module
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<InvoiceItem> InvoiceItems { get; set; }
    public DbSet<UpdDocument> UpdDocuments { get; set; }
    public DbSet<OrderStatusHistory> OrderStatusHistories { get; set; }
    public DbSet<OrderAttachment> OrderAttachments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply configurations
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}

