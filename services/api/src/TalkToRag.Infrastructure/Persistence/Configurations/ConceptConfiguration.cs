using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class ConceptConfiguration : IEntityTypeConfiguration<Concept>
{
    public void Configure(EntityTypeBuilder<Concept> builder)
    {
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(c => new { c.UserId, c.NormalizedName }).IsUnique();
        builder.Property(c => c.State).HasConversion<string>();
        builder.HasMany(c => c.Passages).WithOne().HasForeignKey(p => p.ConceptId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ConceptPrerequisiteConfiguration : IEntityTypeConfiguration<ConceptPrerequisite>
{
    public void Configure(EntityTypeBuilder<ConceptPrerequisite> builder)
    {
        builder.HasKey(e => new { e.ConceptId, e.PrerequisiteId });
        builder.HasOne<Concept>().WithMany().HasForeignKey(e => e.ConceptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Concept>().WithMany().HasForeignKey(e => e.PrerequisiteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(e => e.PrerequisiteId);
        builder.Property(e => e.State).HasConversion<string>();
        builder.ToTable(t => t.HasCheckConstraint("ck_concept_prerequisites_not_self", "concept_id <> prerequisite_id"));
    }
}

public class ConceptPassageConfiguration : IEntityTypeConfiguration<ConceptPassage>
{
    public void Configure(EntityTypeBuilder<ConceptPassage> builder)
    {
        builder.HasOne<Source>().WithMany().HasForeignKey(p => p.SourceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(p => p.SourceId);
    }
}
