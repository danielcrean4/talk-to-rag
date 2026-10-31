using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class ConversationNotebookConfiguration : IEntityTypeConfiguration<ConversationNotebook>
{
    public void Configure(EntityTypeBuilder<ConversationNotebook> builder)
    {
        builder.HasKey(cn => new { cn.ConversationId, cn.NotebookId });
        builder.HasOne<Conversation>().WithMany(c => c.Notebooks).HasForeignKey(cn => cn.ConversationId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Notebook>().WithMany().HasForeignKey(cn => cn.NotebookId).OnDelete(DeleteBehavior.Cascade);
    }
}
