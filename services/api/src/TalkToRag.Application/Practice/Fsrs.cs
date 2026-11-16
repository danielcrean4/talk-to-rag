namespace TalkToRag.Application.Practice;

public enum FsrsRating
{
    Again = 1,
    Hard = 2,
    Good = 3,
    Easy = 4
}

/// <summary>What FSRS knows about one card after its reviews so far.</summary>
public sealed record FsrsCard(double Stability, double Difficulty, DateTimeOffset LastReviewUtc, DateTimeOffset DueUtc);

/// <summary>
/// The FSRS-5 spaced-repetition scheduler with its published default parameters and a target retention of 0.9,
/// ported from open-spaced-repetition's py-fsrs 5.1.3 (MIT), https://github.com/open-spaced-repetition/py-fsrs.
/// It runs without learning steps or fuzzing: every review schedules the card a whole number of days ahead, at least one.
/// </summary>
public static class Fsrs
{
    public const double DesiredRetention = 0.9;
    public const int MaximumIntervalDays = 36500;

    private static readonly double[] W =
    [
        0.40255, 1.18385, 3.173, 15.69105, 7.1949, 0.5345, 1.4604, 0.0046, 1.54575, 0.1192,
        1.01925, 1.9395, 0.11, 0.29605, 2.2698, 0.2315, 2.9898, 0.51655, 0.6621
    ];

    private const double Decay = -0.5;
    private static readonly double Factor = Math.Pow(0.9, 1 / Decay) - 1;

    /// <summary>
    /// Turns an attempt's score into a rating: below 0.25 is Again, below 0.75 is Hard, the rest is Good.
    /// So the practice screen's Missed (0), Almost (0.5) and Got it (1) are Again, Hard and Good. No score gives Easy.
    /// </summary>
    public static FsrsRating RatingFor(double score) => score switch
    {
        < 0.25 => FsrsRating.Again,
        < 0.75 => FsrsRating.Hard,
        _ => FsrsRating.Good
    };

    /// <summary>Replays answers in time order; null when there are none, which means the card is new.</summary>
    public static FsrsCard? Replay(IEnumerable<(DateTimeOffset AnsweredAtUtc, double Score)> answers)
    {
        FsrsCard? card = null;
        foreach (var (answeredAtUtc, score) in answers.OrderBy(a => a.AnsweredAtUtc))
        {
            card = Review(card, RatingFor(score), answeredAtUtc);
        }

        return card;
    }

    public static FsrsCard Review(FsrsCard? card, FsrsRating rating, DateTimeOffset reviewedAtUtc)
    {
        double stability;
        double difficulty;
        if (card is null)
        {
            stability = Math.Max(W[(int)rating - 1], 0.1);
            difficulty = InitialDifficulty(rating);
        }
        else
        {
            var elapsedDays = Math.Max(0, (int)(reviewedAtUtc - card.LastReviewUtc).TotalDays);
            stability = elapsedDays < 1
                ? card.Stability * Math.Exp(W[17] * ((int)rating - 3 + W[18]))
                : NextStability(card, rating, Retrievability(card.Stability, elapsedDays));
            difficulty = NextDifficulty(card.Difficulty, rating);
        }

        return new FsrsCard(stability, difficulty, reviewedAtUtc, reviewedAtUtc.AddDays(IntervalDays(stability)));
    }

    public static int IntervalDays(double stability)
    {
        var days = stability / Factor * (Math.Pow(DesiredRetention, 1 / Decay) - 1);
        return Math.Clamp((int)Math.Round(days, MidpointRounding.ToEven), 1, MaximumIntervalDays);
    }

    private static double Retrievability(double stability, int elapsedDays) =>
        Math.Pow(1 + Factor * elapsedDays / stability, Decay);

    private static double InitialDifficulty(FsrsRating rating) =>
        ClampDifficulty(W[4] - Math.Exp(W[5] * ((int)rating - 1)) + 1);

    private static double NextDifficulty(double difficulty, FsrsRating rating)
    {
        var delta = -W[6] * ((int)rating - 3);
        var damped = difficulty + (10 - difficulty) * delta / 9;
        return ClampDifficulty(W[7] * InitialDifficulty(FsrsRating.Easy) + (1 - W[7]) * damped);
    }

    private static double NextStability(FsrsCard card, FsrsRating rating, double retrievability)
    {
        var (s, d) = (card.Stability, card.Difficulty);
        if (rating == FsrsRating.Again)
        {
            var longTerm = W[11] * Math.Pow(d, -W[12]) * (Math.Pow(s + 1, W[13]) - 1) * Math.Exp((1 - retrievability) * W[14]);
            var shortTerm = s / Math.Exp(W[17] * W[18]);
            return Math.Min(longTerm, shortTerm);
        }

        var hardPenalty = rating == FsrsRating.Hard ? W[15] : 1;
        var easyBonus = rating == FsrsRating.Easy ? W[16] : 1;
        return s * (1 + Math.Exp(W[8]) * (11 - d) * Math.Pow(s, -W[9]) * (Math.Exp((1 - retrievability) * W[10]) - 1) * hardPenalty * easyBonus);
    }

    private static double ClampDifficulty(double difficulty) => Math.Clamp(difficulty, 1, 10);
}
