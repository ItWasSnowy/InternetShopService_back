using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Data.Configurations.Shared;

public class ShopConfiguration : IEntityTypeConfiguration<Shop>
{
    public void Configure(EntityTypeBuilder<Shop> builder)
    {
        builder.ToTable("Shops");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.Domain)
            .HasMaxLength(255);

        builder.Property(x => x.FimBizCompanyId)
            .IsRequired();

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        // Индексы
        builder.HasIndex(x => x.FimBizCompanyId);
        builder.HasIndex(x => new { x.FimBizCompanyId, x.FimBizOrganizationId });
        builder.HasIndex(x => x.Domain)
            .IsUnique()
            .HasFilter("\"Domain\" IS NOT NULL");
        
        builder.HasIndex(x => x.IsActive);
    }
}


