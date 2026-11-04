using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> builder)
    {
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(s => s.Status).HasConversion<string>();
        builder.Property(s => s.FileHash).HasMaxLength(64);
        builder.HasIndex(s => s.BlobKey).IsUnique();
        builder.HasIndex(s => new { s.UserId, s.CreatedAtUtc });
        builder.HasIndex(s => new { s.UserId, s.FileHash });
    }
}
