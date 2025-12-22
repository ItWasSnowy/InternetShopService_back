using InternetShopService_back.Modules.Notifications.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InternetShopService_back.Data.Configurations.Notifications;

public class ShopNotificationConfiguration : IEntityTypeConfiguration<ShopNotification>
{
    public void Configure(EntityTypeBuilder<ShopNotification> builder)
    {
        builder.ToTable("ShopNotifications");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Title)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.ObjectType)
            .HasConversion<int>();

        builder.Property(x => x.IsRead)
            .IsRequired();

        builder.HasIndex(x => x.CounterpartyId);
        builder.HasIndex(x => x.UserAccountId);
        builder.HasIndex(x => x.IsRead);
        builder.HasIndex(x => x.CreatedAt);
        builder.HasIndex(x => new { x.CounterpartyId, x.IsRead, x.CreatedAt });
        builder.HasIndex(x => new { x.CounterpartyId, x.ObjectType, x.ObjectId });
    }
}
