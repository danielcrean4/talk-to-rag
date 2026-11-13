using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TalkToRag.Application.Conversations;

namespace TalkToRag.Api.Controllers;

[Authorize]
[ApiController]
[Route("api/conversations")]
public sealed class ConversationsController(IConversationService conversationService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<ConversationSummaryDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public Task<IReadOnlyList<ConversationSummaryDto>> List(CancellationToken cancellationToken) =>
        conversationService.ListAsync(cancellationToken);

    [HttpGet("{id:guid}")]
    [ProducesResponseType<ConversationDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ConversationDetailsDto> GetById(Guid id, CancellationToken cancellationToken) =>
        conversationService.GetByIdAsync(id, cancellationToken);
}
