using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Shared.Models;

namespace InternetShopService_back.Data.Configurations.Shared;

public class DiscountConfiguration : IEntityTypeConfiguration<Discount>
{
    public void Configure(EntityTypeBuilder<Discount> builder)
    {
        builder.ToTable("Discounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.DiscountPercent)
            .HasPrecision(5, 2);

        builder.HasIndex(x => x.CounterpartyId);
        builder.HasIndex(x => new { x.NomenclatureGroupId, x.NomenclatureId });

        builder.HasOne(x => x.Counterparty)
            .WithMany(x => x.Discounts)
            .HasForeignKey(x => x.CounterpartyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


