using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Data.Configurations.UserCabinet;

public class UserAccountConfiguration : IEntityTypeConfiguration<UserAccount>
{
    public void Configure(EntityTypeBuilder<UserAccount> builder)
    {
        builder.ToTable("UserAccounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.PhoneNumber)
            .IsRequired()
            .HasMaxLength(20);

        builder.Property(x => x.PasswordHash)
            .HasMaxLength(255);

        builder.HasIndex(x => x.PhoneNumber)
            .IsUnique();

        builder.HasOne(x => x.Counterparty)
            .WithOne(x => x.UserAccount)
            .HasForeignKey<UserAccount>(x => x.CounterpartyId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

