using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence.Configurations;

public class QuestionConfiguration : IEntityTypeConfiguration<Question>
{
    public void Configure(EntityTypeBuilder<Question> builder)
    {
        builder.HasOne<User>().WithMany().HasForeignKey(q => q.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Concept>().WithMany().HasForeignKey(q => q.ConceptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Source>().WithMany().HasForeignKey(q => q.SourceId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(q => q.ConceptId);
    }
}

public class AttemptConfiguration : IEntityTypeConfiguration<Attempt>
{
    public void Configure(EntityTypeBuilder<Attempt> builder)
    {
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Concept>().WithMany().HasForeignKey(a => a.ConceptId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Question>().WithMany().HasForeignKey(a => a.QuestionId).OnDelete(DeleteBehavior.SetNull);
        builder.HasIndex(a => new { a.UserId, a.ConceptId, a.AnsweredAtUtc });
        builder.ToTable(t => t.HasCheckConstraint("ck_attempts_score_range", "score >= 0 AND score <= 1"));
    }
}
