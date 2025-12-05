using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Data.Configurations.OrderManagement;

public class UpdDocumentConfiguration : IEntityTypeConfiguration<UpdDocument>
{
    public void Configure(EntityTypeBuilder<UpdDocument> builder)
    {
        builder.ToTable("UpdDocuments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DocumentNumber)
            .IsRequired()
            .HasMaxLength(50);

        builder.HasIndex(x => x.DocumentNumber);
        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.InvoiceId);

        builder.HasOne(x => x.Order)
            .WithOne(x => x.UpdDocument)
            .HasForeignKey<UpdDocument>(x => x.OrderId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Invoice)
            .WithMany()
            .HasForeignKey(x => x.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Counterparty)
            .WithMany()
            .HasForeignKey(x => x.CounterpartyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}


