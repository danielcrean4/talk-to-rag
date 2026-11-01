using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class MessageCitationConfiguration : IEntityTypeConfiguration<MessageCitation>
{
    public void Configure(EntityTypeBuilder<MessageCitation> builder)
    {
        builder.HasKey(mc => new { mc.MessageId, mc.SourceChunkId });
        builder.HasOne<Message>().WithMany(m => m.Citations).HasForeignKey(mc => mc.MessageId).OnDelete(DeleteBehavior.Cascade);
        // Deleting a source deletes its chunks, and with them the citations that pointed at them.
        builder.HasOne<SourceChunk>().WithMany().HasForeignKey(mc => mc.SourceChunkId).OnDelete(DeleteBehavior.Cascade);
    }
}
