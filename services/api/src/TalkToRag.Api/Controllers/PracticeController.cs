using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Practice;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api")]
[ProducesResponseType(StatusCodes.Status401Unauthorized)]
public sealed class PracticeController(IPracticeService practiceService) : ControllerBase
{
    [HttpGet("concepts/{conceptId:guid}/questions")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IReadOnlyList<QuestionDto>> ListQuestions(Guid conceptId, CancellationToken cancellationToken) =>
        practiceService.ListQuestionsAsync(conceptId, cancellationToken);

    [HttpPost("concepts/{conceptId:guid}/questions")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<QuestionDto> AddQuestion(Guid conceptId, CreateQuestionRequest request, CancellationToken cancellationToken) =>
        practiceService.AddQuestionAsync(conceptId, request, cancellationToken);

    [HttpPost("concepts/{conceptId:guid}/questions/generate")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public Task<IReadOnlyList<QuestionDto>> GenerateQuestions(Guid conceptId, CancellationToken cancellationToken) =>
        practiceService.GenerateQuestionsAsync(conceptId, cancellationToken);

    [HttpDelete("questions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteQuestion(Guid id, CancellationToken cancellationToken)
    {
        await practiceService.DeleteQuestionAsync(id, cancellationToken);
        return NoContent();
    }

    /// <summary>Records an answer from flashcards, a quiz or a game, and returns the concept's updated mastery.</summary>
    [HttpPost("attempts")]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<AttemptResultDto> RecordAttempt(RecordAttemptRequest request, CancellationToken cancellationToken) =>
        practiceService.RecordAttemptAsync(request, cancellationToken);
}
