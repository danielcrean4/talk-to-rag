using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class MessageConfiguration : IEntityTypeConfiguration<Message>
{
    public void Configure(EntityTypeBuilder<Message> builder)
    {
        builder.HasOne<Conversation>().WithMany(c => c.Messages).HasForeignKey(m => m.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.Property(m => m.Role).HasConversion<string>();
        builder.HasIndex(m => new { m.ConversationId, m.CreatedAtUtc });
    }
}
