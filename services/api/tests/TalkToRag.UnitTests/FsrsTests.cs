using TalkToRag.Application.Practice;

namespace TalkToRag.UnitTests;

/// <summary>Expected values come from py-fsrs 5.1.3 run with no learning steps and no fuzzing.</summary>
public class FsrsTests
{
    private const double Tolerance = 0.0001;
    private static readonly DateTimeOffset Start = new(2026, 11, 1, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FsrsRating.Again, 1, 0.4026, 7.1949)]
    [InlineData(FsrsRating.Hard, 1, 1.1839, 6.4883)]
    [InlineData(FsrsRating.Good, 3, 3.173, 5.2824)]
    [InlineData(FsrsRating.Easy, 16, 15.6911, 3.2245)]
    public void FirstReview_UsesTheDefaultInitialStabilityAndDifficulty(FsrsRating rating, int days, double stability, double difficulty)
    {
        var card = Fsrs.Review(null, rating, Start);

        Assert.Equal(Start.AddDays(days), card.DueUtc);
        Assert.Equal(stability, card.Stability, Tolerance);
        Assert.Equal(difficulty, card.Difficulty, Tolerance);
    }

    [Fact]
    public void Reviews_GrowTheInterval_ALapseShortensIt_AndASameDayReviewUsesShortTermStability()
    {
        var steps = new (FsrsRating Rating, TimeSpan? After, DateTimeOffset Due, double Stability, double Difficulty)[]
        {
            (FsrsRating.Good, null, D(2026, 11, 4, 9), 3.173, 5.2824),
            (FsrsRating.Good, null, D(2026, 11, 15, 9), 10.7389, 5.273),
            (FsrsRating.Good, null, D(2026, 12, 20, 9), 34.5776, 5.2635),
            (FsrsRating.Again, null, D(2026, 12, 24, 9), 3.8182, 6.7842),
            (FsrsRating.Good, TimeSpan.FromHours(1), D(2026, 12, 25, 10), 5.3751, 6.7679),
            (FsrsRating.Hard, null, D(2027, 1, 1, 10), 7.4075, 7.2736),
        };

        FsrsCard? card = null;
        var at = Start;
        foreach (var step in steps)
        {
            if (step.After is { } after)
            {
                at = card!.LastReviewUtc + after;
            }

            card = Fsrs.Review(card, step.Rating, at);
            Assert.Equal(step.Due, card.DueUtc);
            Assert.Equal(step.Stability, card.Stability, Tolerance);
            Assert.Equal(step.Difficulty, card.Difficulty, Tolerance);
            at = card.DueUtc;
        }
    }

    [Fact]
    public void Replay_SortsAnswersByTime()
    {
        (DateTimeOffset, double)[] answers = [(Start, 1), (Start.AddDays(3), 1), (Start.AddDays(14), 0)];

        var inOrder = Fsrs.Replay(answers);
        var shuffled = Fsrs.Replay([answers[2], answers[0], answers[1]]);

        Assert.Equal(inOrder, shuffled);
        Assert.Equal(Start.AddDays(14), inOrder!.LastReviewUtc);
        Assert.Null(Fsrs.Replay([]));
    }

    [Theory]
    [InlineData(0, FsrsRating.Again)]
    [InlineData(0.24, FsrsRating.Again)]
    [InlineData(0.25, FsrsRating.Hard)]
    [InlineData(0.5, FsrsRating.Hard)]
    [InlineData(0.75, FsrsRating.Good)]
    [InlineData(1, FsrsRating.Good)]
    public void RatingFor_MapsTheScoreBands(double score, FsrsRating rating) =>
        Assert.Equal(rating, Fsrs.RatingFor(score));

    private static DateTimeOffset D(int year, int month, int day, int hour) => new(year, month, day, hour, 0, 0, TimeSpan.Zero);
}
