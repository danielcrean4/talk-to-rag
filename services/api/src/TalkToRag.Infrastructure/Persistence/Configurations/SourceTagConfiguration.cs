using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class SourceTagConfiguration : IEntityTypeConfiguration<SourceTag>
{
    public void Configure(EntityTypeBuilder<SourceTag> builder)
    {
        builder.HasKey(st => new { st.SourceId, st.TagId });
        builder.HasOne<Source>().WithMany(s => s.Tags).HasForeignKey(st => st.SourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tag>().WithMany().HasForeignKey(st => st.TagId).OnDelete(DeleteBehavior.Cascade);
    }
}
