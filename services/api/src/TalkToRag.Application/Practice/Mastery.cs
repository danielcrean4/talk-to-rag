using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Practice;

/// <summary>
/// How well a concept is understood, worked out from attempts.
/// Level is "New" (never tried), "Learning", or "Solid" (the recent attempts were mostly right).
/// </summary>
public sealed record MasteryDto(string Level, int Attempts, double? RecentScore, DateTimeOffset? LastPractisedAtUtc)
{
    public const int RecentWindow = 5;
    public const int SolidAfterAttempts = 3;
    public const double SolidScore = 0.8;

    public static readonly MasteryDto None = new("New", 0, null, null);

    public static MasteryDto From(IReadOnlyCollection<Attempt> attempts)
    {
        if (attempts.Count == 0)
        {
            return None;
        }

        var recent = attempts.OrderByDescending(a => a.AnsweredAtUtc).Take(RecentWindow).ToList();
        var score = recent.Average(a => a.Score);
        var solid = attempts.Count >= SolidAfterAttempts && score >= SolidScore;
        return new MasteryDto(solid ? "Solid" : "Learning", attempts.Count, Math.Round(score, 2), recent[0].AnsweredAtUtc);
    }
}
