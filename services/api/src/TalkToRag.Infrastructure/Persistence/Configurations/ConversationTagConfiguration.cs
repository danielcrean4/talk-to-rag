using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class ConversationTagConfiguration : IEntityTypeConfiguration<ConversationTag>
{
    public void Configure(EntityTypeBuilder<ConversationTag> builder)
    {
        builder.HasKey(ct => new { ct.ConversationId, ct.TagId });
        builder.HasOne<Conversation>().WithMany(c => c.Tags).HasForeignKey(ct => ct.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Tag>().WithMany().HasForeignKey(ct => ct.TagId).OnDelete(DeleteBehavior.Cascade);
    }
}
