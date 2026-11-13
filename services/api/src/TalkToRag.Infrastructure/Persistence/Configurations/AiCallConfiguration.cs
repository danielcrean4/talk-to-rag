using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class AiCallConfiguration : IEntityTypeConfiguration<AiCall>
{
    public void Configure(EntityTypeBuilder<AiCall> builder)
    {
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.SetNull);
        builder.Property(c => c.Request).HasColumnType("jsonb");
        builder.Property(c => c.Response).HasColumnType("jsonb");
        builder.Property(c => c.RequestRedacted).HasColumnType("jsonb");
        builder.Property(c => c.ResponseRedacted).HasColumnType("jsonb");
        builder.HasIndex(c => c.CreatedAtUtc);
        builder.HasIndex(c => new { c.Operation, c.CreatedAtUtc });
    }
}
