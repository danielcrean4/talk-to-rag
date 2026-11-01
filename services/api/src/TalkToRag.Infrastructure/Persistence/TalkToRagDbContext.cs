using Microsoft.EntityFrameworkCore;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence;

/// <summary>The only owner of the Postgres schema. Python never touches the database.</summary>
public class TalkToRagDbContext(DbContextOptions<TalkToRagDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Notebook> Notebooks => Set<Notebook>();
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<SourceChunk> SourceChunks => Set<SourceChunk>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<NotebookSource> NotebookSources => Set<NotebookSource>();
    public DbSet<SourceTag> SourceTags => Set<SourceTag>();
    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<ConversationNotebook> ConversationNotebooks => Set<ConversationNotebook>();
    public DbSet<ConversationTag> ConversationTags => Set<ConversationTag>();
    public DbSet<Message> Messages => Set<Message>();
    public DbSet<MessageCitation> MessageCitations => Set<MessageCitation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // CREATE EXTENSION vector, emitted by the first migration.
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(TalkToRagDbContext).Assembly);
    }
}
