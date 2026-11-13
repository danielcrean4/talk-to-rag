using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class MessageCitationConfiguration : IEntityTypeConfiguration<MessageCitation>
{
    public void Configure(EntityTypeBuilder<MessageCitation> builder)
    {
        builder.HasKey(mc => new { mc.MessageId, mc.Number });
        builder.HasOne<Message>().WithMany(m => m.Citations).HasForeignKey(mc => mc.MessageId).OnDelete(DeleteBehavior.Cascade);
        // Re-ingesting a source replaces its chunks; the citation keeps its own copy of the passage.
        builder.HasOne<SourceChunk>().WithMany().HasForeignKey(mc => mc.SourceChunkId).OnDelete(DeleteBehavior.SetNull);
        builder.HasOne<Source>().WithMany().HasForeignKey(mc => mc.SourceId).OnDelete(DeleteBehavior.Cascade);
    }
}
