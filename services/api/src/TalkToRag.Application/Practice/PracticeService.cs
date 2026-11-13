using TalkToRag.Application.Common.Exceptions;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Application.Practice;

public sealed class PracticeService(
    IPracticeRepository practiceRepository,
    IConceptRepository conceptRepository,
    ISourceRepository sourceRepository,
    IAiClient aiClient,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IPracticeService
{
    private const int PassagesPerGeneration = 3;
    private const int PassageTextLength = 2500;
    private const int QuestionsPerGeneration = 4;
    private const int MaxPlatformLength = 60;

    public async Task<IReadOnlyList<QuestionDto>> ListQuestionsAsync(Guid conceptId, CancellationToken cancellationToken)
    {
        await RequireConceptAsync(conceptId, cancellationToken);
        var questions = await practiceRepository.ListQuestionsAsync(conceptId, currentUser.UserId, cancellationToken);
        var attempts = await practiceRepository.ListAttemptsAsync(currentUser.UserId, cancellationToken);
        var titles = await TitlesAsync(questions, cancellationToken);
        return questions.Select(q => ToDto(q, titles, attempts)).ToList();
    }

    public async Task<QuestionDto> AddQuestionAsync(Guid conceptId, CreateQuestionRequest request, CancellationToken cancellationToken)
    {
        await RequireConceptAsync(conceptId, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.Prompt) || string.IsNullOrWhiteSpace(request.Answer))
        {
            throw new RequestValidationException("A question needs both a prompt and an answer.");
        }

        var question = NewQuestion(conceptId, request.Prompt, request.Answer, generatedByModel: false, place: null);
        practiceRepository.Add(question);
        await practiceRepository.SaveChangesAsync(cancellationToken);
        return ToDto(question, [], []);
    }

    public async Task<IReadOnlyList<QuestionDto>> GenerateQuestionsAsync(Guid conceptId, CancellationToken cancellationToken)
    {
        var concept = await RequireConceptAsync(conceptId, cancellationToken);
        var places = concept.Passages.OrderBy(p => p.CreatedAtUtc).Take(PassagesPerGeneration).ToList();
        if (places.Count == 0)
        {
            throw new RequestValidationException("Link at least one passage first, so the questions come from your sources.");
        }

        var passages = new List<QuestionPassageDto>();
        foreach (var (place, index) in places.Select((place, index) => (place, index)))
        {
            var text = await practiceRepository.GetTextAtAsync(place.SourceId, place.PageNumber, place.Section, PassageTextLength, cancellationToken);
            passages.Add(new QuestionPassageDto(index + 1, string.IsNullOrWhiteSpace(text) ? place.Snippet : text));
        }

        var response = await aiClient.GenerateQuestionsAsync(
            new GenerateQuestionsRequest(concept.Name, passages, QuestionsPerGeneration), cancellationToken);

        var existing = await practiceRepository.ListQuestionsAsync(conceptId, currentUser.UserId, cancellationToken);
        var known = existing.Select(q => Concept.Normalize(q.Prompt)).ToHashSet();
        var added = new List<Question>();
        foreach (var generated in response.Questions)
        {
            if (string.IsNullOrWhiteSpace(generated.Prompt) || string.IsNullOrWhiteSpace(generated.Answer) || !known.Add(Concept.Normalize(generated.Prompt)))
            {
                continue;
            }

            var place = generated.Passage is { } n && n >= 1 && n <= places.Count ? places[n - 1] : null;
            var question = NewQuestion(conceptId, generated.Prompt, generated.Answer, generatedByModel: true, place);
            practiceRepository.Add(question);
            added.Add(question);
        }

        await practiceRepository.SaveChangesAsync(cancellationToken);
        var titles = await TitlesAsync(added, cancellationToken);
        return added.Select(q => ToDto(q, titles, [])).ToList();
    }

    public async Task DeleteQuestionAsync(Guid id, CancellationToken cancellationToken)
    {
        var question = await practiceRepository.FindQuestionAsync(id, currentUser.UserId, cancellationToken)
            ?? throw new NotFoundException($"Question '{id}' not found.");
        practiceRepository.Remove(question);
        await practiceRepository.SaveChangesAsync(cancellationToken);
    }

    public async Task<AttemptResultDto> RecordAttemptAsync(RecordAttemptRequest request, CancellationToken cancellationToken)
    {
        var platform = request.Platform?.Trim();
        if (string.IsNullOrEmpty(platform) || platform.Length > MaxPlatformLength)
        {
            throw new RequestValidationException($"Say where the attempt happened (platform), in at most {MaxPlatformLength} characters.");
        }

        if (double.IsNaN(request.Score) || request.Score < 0 || request.Score > 1)
        {
            throw new RequestValidationException("Score runs from 0 (wrong) to 1 (right).");
        }

        Guid conceptId;
        if (request.QuestionId is { } questionId)
        {
            var question = await practiceRepository.FindQuestionAsync(questionId, currentUser.UserId, cancellationToken)
                ?? throw new NotFoundException($"Question '{questionId}' not found.");
            conceptId = question.ConceptId;
        }
        else if (request.ConceptId is { } requestedConceptId)
        {
            conceptId = (await RequireConceptAsync(requestedConceptId, cancellationToken)).Id;
        }
        else
        {
            throw new RequestValidationException("An attempt needs a question or a concept.");
        }

        var attempt = new Attempt
        {
            Id = Guid.CreateVersion7(),
            UserId = currentUser.UserId,
            ConceptId = conceptId,
            QuestionId = request.QuestionId,
            Platform = platform,
            Score = request.Score,
            AnsweredAtUtc = timeProvider.GetUtcNow()
        };
        practiceRepository.Add(attempt);
        await practiceRepository.SaveChangesAsync(cancellationToken);

        var attempts = await practiceRepository.ListAttemptsAsync(currentUser.UserId, cancellationToken);
        return new AttemptResultDto(conceptId, MasteryDto.From(attempts.Where(a => a.ConceptId == conceptId).ToList()));
    }

    private async Task<Concept> RequireConceptAsync(Guid conceptId, CancellationToken cancellationToken)
    {
        var concepts = await conceptRepository.ListForUserAsync(currentUser.UserId, cancellationToken);
        return concepts.FirstOrDefault(c => c.Id == conceptId) ?? throw new NotFoundException($"Concept '{conceptId}' not found.");
    }

    private async Task<Dictionary<Guid, string>> TitlesAsync(IEnumerable<Question> questions, CancellationToken cancellationToken)
    {
        var titles = new Dictionary<Guid, string>();
        foreach (var sourceId in questions.Select(q => q.SourceId).OfType<Guid>().Distinct())
        {
            var source = await sourceRepository.FindByIdForUserAsync(sourceId, currentUser.UserId, cancellationToken);
            if (source is not null)
            {
                titles[sourceId] = source.Title;
            }
        }

        return titles;
    }

    private Question NewQuestion(Guid conceptId, string prompt, string answer, bool generatedByModel, ConceptPassage? place) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = currentUser.UserId,
        ConceptId = conceptId,
        Prompt = prompt.Trim(),
        Answer = answer.Trim(),
        GeneratedByModel = generatedByModel,
        SourceId = place?.SourceId,
        PageNumber = place?.PageNumber,
        PageLabel = place?.PageLabel,
        Section = place?.Section,
        CreatedAtUtc = timeProvider.GetUtcNow()
    };

    private static QuestionDto ToDto(Question question, Dictionary<Guid, string> titles, IReadOnlyCollection<Attempt> attempts)
    {
        var own = attempts.Where(a => a.QuestionId == question.Id).OrderByDescending(a => a.AnsweredAtUtc).ToList();
        return new QuestionDto(
            question.Id,
            question.ConceptId,
            question.Prompt,
            question.Answer,
            question.GeneratedByModel,
            question.SourceId,
            question.SourceId is { } sourceId ? titles.GetValueOrDefault(sourceId) : null,
            question.PageNumber,
            question.PageLabel,
            question.Section,
            own.Count,
            own.Count > 0 ? own[0].Score : null);
    }
}
