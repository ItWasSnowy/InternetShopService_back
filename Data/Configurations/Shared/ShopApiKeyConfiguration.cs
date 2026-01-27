using InternetShopService_back.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternetShopService_back.Data.Configurations.Shared;

public class ShopApiKeyConfiguration : IEntityTypeConfiguration<ShopApiKey>
{
    public void Configure(EntityTypeBuilder<ShopApiKey> builder)
    {
        builder.ToTable("ShopApiKeys");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ApiKey)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(x => x.IsActive)
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(x => x.ShopId);
        builder.HasIndex(x => x.ApiKey)
            .IsUnique();

        builder.HasOne(x => x.Shop)
            .WithMany()
            .HasForeignKey(x => x.ShopId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
