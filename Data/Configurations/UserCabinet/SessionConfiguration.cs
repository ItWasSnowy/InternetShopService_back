using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Modules.UserCabinet.Models;

namespace InternetShopService_back.Data.Configurations.UserCabinet;

public class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AccessToken)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.RefreshToken)
            .IsRequired()
            .HasMaxLength(500);

        builder.HasIndex(x => x.AccessToken);
        builder.HasIndex(x => x.RefreshToken);
        builder.HasIndex(x => x.UserAccountId);

        builder.HasOne(x => x.UserAccount)
            .WithMany(x => x.Sessions)
            .HasForeignKey(x => x.UserAccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}


