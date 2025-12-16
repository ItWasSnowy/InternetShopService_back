using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using InternetShopService_back.Modules.OrderManagement.Models;

namespace InternetShopService_back.Data.Configurations.OrderManagement;

public class OrderCommentConfiguration : IEntityTypeConfiguration<OrderComment>
{
    public void Configure(EntityTypeBuilder<OrderComment> builder)
    {
        builder.ToTable("OrderComments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExternalCommentId)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(x => x.CommentText)
            .IsRequired()
            .HasMaxLength(5000);

        builder.Property(x => x.AuthorName)
            .HasMaxLength(200);

        builder.HasIndex(x => x.OrderId);
        builder.HasIndex(x => x.AuthorUserId);
        builder.HasIndex(x => x.ExternalCommentId)
            .IsUnique();
        builder.HasIndex(x => x.CreatedAt);

        builder.HasOne(x => x.Order)
            .WithMany(o => o.Comments)
            .HasForeignKey(x => x.OrderId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

public class OrderCommentAttachmentConfiguration : IEntityTypeConfiguration<OrderCommentAttachment>
{
    public void Configure(EntityTypeBuilder<OrderCommentAttachment> builder)
    {
        builder.ToTable("OrderCommentAttachments");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(x => x.ContentType)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(x => x.FileUrl)
            .IsRequired()
            .HasMaxLength(2000);

        builder.HasIndex(x => x.OrderCommentId);

        builder.HasOne(x => x.OrderComment)
            .WithMany(c => c.Attachments)
            .HasForeignKey(x => x.OrderCommentId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

