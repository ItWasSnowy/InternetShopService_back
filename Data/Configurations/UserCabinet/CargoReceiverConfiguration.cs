using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Data.Configurations.UserCabinet;

public class CargoReceiverConfiguration : IEntityTypeConfiguration<CargoReceiver>
{
    public void Configure(EntityTypeBuilder<CargoReceiver> builder)
    {
        builder.ToTable("CargoReceivers");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FullName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.PassportSeries)
            .IsRequired()
            .HasMaxLength(10);

        builder.Property(x => x.PassportNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.PassportIssuedBy)
            .HasMaxLength(500);

        builder.HasOne(x => x.UserAccount)
            .WithMany(x => x.CargoReceivers)
            .HasForeignKey(x => x.UserAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


