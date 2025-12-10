using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Data.Configurations.OrderManagement;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PdfUrl)
            .HasMaxLength(500)
            .IsRequired(false);

        builder.HasIndex(x => x.OrderId)
            .IsUnique();

        builder.HasOne(x => x.Order)
            .WithOne(x => x.Invoice)
            .HasForeignKey<Invoice>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


