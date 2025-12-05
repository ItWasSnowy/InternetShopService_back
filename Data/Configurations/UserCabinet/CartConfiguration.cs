using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Data.Configurations.UserCabinet;

public class CartConfiguration : IEntityTypeConfiguration<Cart>
{
    public void Configure(EntityTypeBuilder<Cart> builder)
    {
        builder.ToTable("Carts");

        builder.HasKey(x => x.Id);

        builder.HasOne(x => x.UserAccount)
            .WithOne(x => x.Cart)
            .HasForeignKey<Cart>(x => x.UserAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

