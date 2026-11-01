using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class NotebookSourceConfiguration : IEntityTypeConfiguration<NotebookSource>
{
    public void Configure(EntityTypeBuilder<NotebookSource> builder)
    {
        builder.HasKey(ns => new { ns.NotebookId, ns.SourceId });
        builder.HasOne<Notebook>().WithMany(n => n.Sources).HasForeignKey(ns => ns.NotebookId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Source>().WithMany(s => s.Notebooks).HasForeignKey(ns => ns.SourceId).OnDelete(DeleteBehavior.Cascade);
    }
}
